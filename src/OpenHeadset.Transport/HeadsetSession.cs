using System.Threading.Channels;
using OpenHeadset.Core;

namespace OpenHeadset.Transport;

public enum ConnectionStatus
{
    Searching,
    Connecting,
    Connected,
}

/// <param name="Message">Texto pronto para a interface, em português.</param>
public sealed record SessionSnapshot(ConnectionStatus Status, string Message, DetectedHeadset? Headset, HeadsetState? State);

/// <param name="Ok">Verdadeiro só se o fone confirmou o valor na leitura de volta.</param>
public sealed record CommandResult(bool Ok, string Message);

/// <summary>
/// Mantém a conexão com o fone: procura entre os perfis dados, conecta, reconecta e mantém o estado atualizado.
/// Os eventos disparam fora da thread de interface.
/// </summary>
public sealed class HeadsetSession : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);
    private const int MaxConsecutiveTimeouts = 3;

    private readonly IReadOnlyList<HeadsetProfile> _profiles;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<bool> _refreshRequests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private volatile ActiveConnection? _active;
    private Task? _runner;

    private sealed record ActiveConnection(HeadsetLink Link, IHeadsetProtocol Protocol);

    public HeadsetSession(IReadOnlyList<HeadsetProfile> profiles)
    {
        _profiles = profiles;
        Snapshot = new SessionSnapshot(ConnectionStatus.Searching, "Procurando o fone pareado…", null, null);
    }

    public event Action<SessionSnapshot>? Changed;

    public SessionSnapshot Snapshot { get; private set; }

    public void Start() => _runner ??= Task.Run(() => RunAsync(_cts.Token));

    public Task<CommandResult> SetNoiseAsync(NoiseSetting desired) =>
        WriteAndVerifyAsync(
            (protocol, state) => protocol.SetNoise(desired, state),
            state => state.Noise is { } n && n.Mode == desired.Mode && (desired.LevelId is null || n.LevelId == desired.LevelId));

    public Task<CommandResult> SetGameModeAsync(bool enabled) =>
        WriteAndVerifyAsync(
            (protocol, _) => protocol.SetGameMode(enabled),
            state => state.GameMode == enabled);

    private async Task<CommandResult> WriteAndVerifyAsync(
        Func<IHeadsetProtocol, HeadsetState?, ProtocolRequest?> build,
        Func<HeadsetState, bool> confirmed)
    {
        var active = _active;
        if (active is null)
            return new CommandResult(false, "O fone não está conectado.");

        var request = build(active.Protocol, Snapshot.State);
        if (request is null)
            return new CommandResult(false, "Este fone não suporta esse controle.");

        try
        {
            await active.Link.RequestAsync(request, CommandTimeout, _cts.Token).ConfigureAwait(false);
            var state = await ReadStateAsync(active, _cts.Token).ConfigureAwait(false);
            Publish(ConnectionStatus.Connected, "Conectado", Snapshot.Headset, state);

            return confirmed(state)
                ? new CommandResult(true, "Aplicado.")
                : new CommandResult(false, "O fone respondeu, mas o estado não mudou como pedido.");
        }
        catch (TimeoutException)
        {
            return new CommandResult(false, "O fone não respondeu ao comando.");
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or InvalidOperationException)
        {
            return new CommandResult(false, "A conexão com o fone caiu durante o comando.");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var retry = TimeSpan.FromSeconds(2);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                Publish(ConnectionStatus.Searching, "Procurando o fone pareado…", null, null);
                var found = await Task.Run(() => HeadsetLocator.Find(_profiles), ct).ConfigureAwait(false);
                if (found is null)
                {
                    Publish(ConnectionStatus.Searching,
                        "Nenhum fone compatível encontrado. Pareie o fone nas configurações de Bluetooth do Windows.", null, null);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                    continue;
                }

                Publish(ConnectionStatus.Connecting, $"Conectando a {found.Name}…", found, null);
                var protocol = found.Profile.CreateProtocol();
                HeadsetLink link;
                try
                {
                    link = await Task.Run(() => HeadsetLink.Open(found.PortName, found.Profile.Serial, protocol.CreateParser()), ct)
                        .ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    Publish(ConnectionStatus.Connecting,
                        $"Não consegui abrir {found.PortName}. O fone está ligado, por perto e livre de outros programas?", found, null);
                    await Task.Delay(retry, ct).ConfigureAwait(false);
                    retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 20));
                    continue;
                }

                retry = TimeSpan.FromSeconds(2);
                try
                {
                    await RunConnectedAsync(new ActiveConnection(link, protocol), found, ct).ConfigureAwait(false);
                }
                finally
                {
                    _active = null;
                    link.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunConnectedAsync(ActiveConnection active, DetectedHeadset headset, CancellationToken ct)
    {
        var closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        active.Link.Closed += reason => closed.TrySetResult(reason);
        active.Link.MessageReceived += message =>
        {
            // O fone avisa quando algo muda (botão, outro dispositivo): atualiza já, sem esperar o ciclo.
            if (active.Protocol.IsStateChangeHint(message))
                _refreshRequests.Writer.TryWrite(true);
        };
        _active = active;

        var timeouts = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var state = await ReadStateAsync(active, ct).ConfigureAwait(false);
                timeouts = 0;
                Publish(ConnectionStatus.Connected, "Conectado", headset, state);
            }
            catch (TimeoutException) when (++timeouts < MaxConsecutiveTimeouts)
            {
                // Uma falha isolada não derruba a conexão.
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Publish(ConnectionStatus.Connecting, "O fone parou de responder. Reconectando…", headset, null);
                return;
            }

            using var iteration = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var wake = await Task.WhenAny(
                _refreshRequests.Reader.WaitToReadAsync(iteration.Token).AsTask(),
                Task.Delay(PollInterval, iteration.Token),
                closed.Task).ConfigureAwait(false);
            await iteration.CancelAsync().ConfigureAwait(false);

            if (wake == closed.Task)
            {
                Publish(ConnectionStatus.Connecting, "Conexão com o fone perdida. Reconectando…", headset, null);
                return;
            }

            while (_refreshRequests.Reader.TryRead(out _)) { }
        }
    }

    private static async Task<HeadsetState> ReadStateAsync(ActiveConnection active, CancellationToken ct)
    {
        var reply = await active.Link.RequestAsync(active.Protocol.ReadState(), CommandTimeout, ct).ConfigureAwait(false);
        return active.Protocol.DecodeState(reply)
            ?? throw new IOException("Resposta de estado fora do formato esperado.");
    }

    private void Publish(ConnectionStatus status, string message, DetectedHeadset? headset, HeadsetState? state)
    {
        var snapshot = new SessionSnapshot(status, message, headset, state);
        Snapshot = snapshot;
        Changed?.Invoke(snapshot);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _active?.Link.Dispose();
    }
}
