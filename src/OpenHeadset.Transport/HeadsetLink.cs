using OpenHeadset.Core;

namespace OpenHeadset.Transport;

/// <summary>
/// Conexão serial com o fone. Não conhece nenhum protocolo: recebe um <see cref="IMessageParser"/>
/// e trabalha com <see cref="ProtocolRequest"/>. Toda E/S acontece fora da thread de interface.
/// Aceita uma requisição por vez.
/// </summary>
/// <remarks>
/// Todo pedido tem prazo, inclusive a espera pela vez e a escrita. Uma porta Bluetooth cujo fone foi desligado
/// pode deixar a escrita bloqueada sem limite; sem prazo ali, um pedido preso seguraria a fila para sempre
/// e o aplicativo nunca perceberia que a conexão caiu.
/// </remarks>
public sealed class HeadsetLink : IDisposable
{
    private readonly ISerialChannel _channel;
    private readonly IMessageParser _parser;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly CancellationTokenSource _cts = new();
    private TaskCompletionSource<object>? _pending;
    private Func<object, bool>? _pendingMatch;
    private int _closed;

    /// <summary>Tudo que chega do fone: respostas e avisos espontâneos, já interpretados pelo protocolo.</summary>
    public event Action<object>? MessageReceived;

    /// <summary>Disparado uma vez quando a conexão cai.</summary>
    public event Action<string>? Closed;

    internal HeadsetLink(ISerialChannel channel, IMessageParser parser)
    {
        _channel = channel;
        _parser = parser;
        _ = Task.Run(ReadLoop);
    }

    /// <summary>Abre a porta. Lança <see cref="IOException"/> ou <see cref="UnauthorizedAccessException"/> se o fone estiver desligado ou a porta ocupada.</summary>
    public static HeadsetLink Open(string portName, SerialSettings settings, IMessageParser parser) =>
        new(SerialPortChannel.Open(portName, settings.BaudRate), parser);

    /// <summary>
    /// Envia o pedido e espera a mensagem que o protocolo reconhece como resposta. O prazo vale para o total
    /// (esperar a vez, escrever e esperar a resposta). Sem resposta: <see cref="TimeoutException"/>.
    /// Se não for possível nem escrever, a conexão é considerada perdida e é fechada.
    /// </summary>
    public async Task<object> RequestAsync(ProtocolRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        deadline.CancelAfter(timeout);

        try
        {
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ThrowIfClosed();
            throw new TimeoutException("Outro pedido está preso; esta conexão provavelmente caiu.");
        }

        try
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingLock)
            {
                _pending = tcs;
                _pendingMatch = request.IsReply;
            }

            await WriteAsync(request.Frame, deadline.Token, ct).ConfigureAwait(false);

            try
            {
                return await tcs.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                ThrowIfClosed();
                throw new TimeoutException($"O fone não respondeu em {timeout.TotalSeconds:0.#} s.");
            }
        }
        finally
        {
            lock (_pendingLock)
            {
                _pending = null;
                _pendingMatch = null;
            }
            _gate.Release();
        }
    }

    /// <summary>A escrita roda fora da thread de quem chamou e é abandonada se passar do prazo.</summary>
    private async Task WriteAsync(byte[] frame, CancellationToken deadline, CancellationToken callerToken)
    {
        var write = Task.Run(() => _channel.Write(frame, 0, frame.Length));
        try
        {
            await write.WaitAsync(deadline).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            // Escrita travada: a porta está morta. Fechar a libera e faz a Task terminar com erro, que é ignorado.
            _ = write.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            Close("O fone não aceitou o envio (conexão perdida).");
            throw new TimeoutException("O fone não aceitou o envio; a conexão foi considerada perdida.");
        }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or ObjectDisposedException)
        {
            Close($"Falha ao enviar: {e.Message}");
            throw;
        }
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref _closed) == 1)
            throw new IOException("A conexão com o fone foi encerrada.");
    }

    private void ReadLoop()
    {
        var buffer = new byte[512];
        var ct = _cts.Token;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var n = _channel.Read(buffer, 0, buffer.Length);
                if (n == 0)
                {
                    Close("O fone encerrou a conexão.");
                    return;
                }

                foreach (var message in _parser.Feed(buffer.AsSpan(0, n)))
                    Dispatch(message);
            }
            catch (TimeoutException)
            {
                // Sem dados no intervalo: segue escutando.
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or ObjectDisposedException)
            {
                Close($"Conexão perdida: {e.Message}");
                return;
            }
        }
    }

    private void Dispatch(object message)
    {
        TaskCompletionSource<object>? match = null;
        lock (_pendingLock)
        {
            if (_pending is { } p && _pendingMatch is { } isReply && isReply(message))
            {
                match = p;
                _pending = null;
            }
        }

        match?.TrySetResult(message);
        MessageReceived?.Invoke(message);
    }

    private void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;

        _cts.Cancel();
        lock (_pendingLock)
            _pending?.TrySetException(new IOException(reason));

        // Fechar uma porta BT que sumiu pode bloquear; faz fora da thread de quem chamou.
        _ = Task.Run(_channel.Dispose);

        Closed?.Invoke(reason);
    }

    public void Dispose() => Close("Fechado pelo aplicativo.");
}
