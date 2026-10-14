using System.IO.Ports;
using OpenHeadset.Core;

namespace OpenHeadset.Transport;

/// <summary>
/// Conexão serial com o fone. Não conhece nenhum protocolo: recebe um <see cref="IMessageParser"/>
/// e trabalha com <see cref="ProtocolRequest"/>. Toda E/S acontece fora da thread de interface.
/// Aceita uma requisição por vez; mensagens que não são resposta ao pedido em curso só são repassadas por evento.
/// </summary>
public sealed class HeadsetLink : IDisposable
{
    private readonly SerialPort _port;
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

    private HeadsetLink(SerialPort port, IMessageParser parser)
    {
        _port = port;
        _parser = parser;
    }

    /// <summary>Abre a porta. Lança <see cref="IOException"/> ou <see cref="UnauthorizedAccessException"/> se o fone estiver desligado ou a porta ocupada.</summary>
    public static HeadsetLink Open(string portName, SerialSettings settings, IMessageParser parser)
    {
        var port = new SerialPort(portName, settings.BaudRate)
        {
            ReadTimeout = 500,
            WriteTimeout = 3000,
            DtrEnable = true,
            RtsEnable = true,
        };
        port.Open();

        var link = new HeadsetLink(port, parser);
        _ = Task.Run(link.ReadLoop);
        return link;
    }

    /// <summary>Envia o pedido e espera a mensagem que o protocolo reconhece como resposta.</summary>
    public async Task<object> RequestAsync(ProtocolRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingLock)
            {
                _pending = tcs;
                _pendingMatch = request.IsReply;
            }

            try
            {
                await _port.BaseStream.WriteAsync(request.Frame, ct).ConfigureAwait(false);
                await _port.BaseStream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
            {
                Close($"Falha ao enviar: {e.Message}");
                throw;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            timeoutCts.CancelAfter(timeout);
            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
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

    private void ReadLoop()
    {
        var buffer = new byte[512];
        var ct = _cts.Token;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var n = _port.BaseStream.Read(buffer, 0, buffer.Length);
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
        _ = Task.Run(() =>
        {
            try { _port.Close(); } catch { /* já caiu */ }
            try { _port.Dispose(); } catch { /* idem */ }
        });

        Closed?.Invoke(reason);
    }

    public void Dispose() => Close("Fechado pelo aplicativo.");
}
