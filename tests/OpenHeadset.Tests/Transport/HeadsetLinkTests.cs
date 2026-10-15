using System.Collections.Concurrent;
using System.Diagnostics;
using OpenHeadset.Core;
using OpenHeadset.Transport;

namespace OpenHeadset.Tests.Transport;

/// <summary>
/// Regressão de um defeito real: com o fone desligado, a escrita na porta Bluetooth ficava bloqueada para sempre,
/// o pedido nunca expirava, segurava a fila e o aplicativo nunca percebia que a conexão caiu.
/// </summary>
public class HeadsetLinkTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(400);

    /// <summary>Cada byte recebido vira uma mensagem (um byte[1]). Basta para exercitar o fluxo.</summary>
    private sealed class EchoParser : IMessageParser
    {
        public IReadOnlyList<object> Feed(ReadOnlySpan<byte> data) => data.ToArray().Select(b => (object)new[] { b }).ToList();
        public void Reset() { }
    }

    /// <summary>Segura os bytes e só os entrega no <see cref="Flush"/>, como um quadro curto que o cabeçalho não descreve.</summary>
    private sealed class HoldUntilFlushParser : IMessageParser
    {
        private readonly List<byte> _held = [];
        public IReadOnlyList<object> Feed(ReadOnlySpan<byte> data) { _held.AddRange(data.ToArray()); return []; }
        public IReadOnlyList<object> Flush()
        {
            var message = _held.Count > 0 ? new object[] { _held.ToArray() } : [];
            _held.Clear();
            return message;
        }
        public void Reset() => _held.Clear();
    }

    private static ProtocolRequest Request(byte id) => new([id], m => m is byte[] { Length: 1 } b && b[0] == id);

    /// <summary>Porta de mentira. O comportamento de escrita e de resposta é escolhido por teste.</summary>
    private sealed class FakeChannel : ISerialChannel
    {
        private readonly BlockingCollection<byte[]> _incoming = [];
        private readonly ManualResetEventSlim _released = new(false);
        private volatile bool _disposed;

        public Action<byte[]> OnWrite { get; set; } = _ => { };
        public int Writes;

        public void Inject(params byte[] bytes) => _incoming.Add(bytes);

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FakeChannel));
            if (!_incoming.TryTake(out var chunk, 100))
                throw new TimeoutException();
            chunk.CopyTo(buffer, offset);
            return chunk.Length;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            Interlocked.Increment(ref Writes);
            OnWrite(buffer.AsSpan(offset, count).ToArray());
        }

        /// <summary>Faz a escrita bloquear até a porta ser fechada, como uma porta BT cujo fone sumiu.</summary>
        public void BlockWritesUntilClosed() => OnWrite = _ =>
        {
            _released.Wait();
            throw new IOException("porta fechada");
        };

        public void Dispose()
        {
            _disposed = true;
            _released.Set();
        }
    }

    private static async Task<T> Within<T>(Task<T> task, TimeSpan limit)
    {
        var finished = await Task.WhenAny(task, Task.Delay(limit));
        Assert.True(finished == task, $"A operação não terminou em {limit.TotalSeconds:0.#} s (travou).");
        return await task;
    }

    [Fact]
    public async Task A_normal_request_gets_its_reply()
    {
        var channel = new FakeChannel();
        channel.OnWrite = frame => channel.Inject(frame[0]);
        using var link = new HeadsetLink(channel, new EchoParser());

        var reply = await Within(link.RequestAsync(Request(7), TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(3));

        Assert.Equal(new byte[] { 7 }, Assert.IsType<byte[]>(reply));
    }

    [Fact]
    public async Task A_reply_the_parser_only_completes_on_flush_still_answers_the_request()
    {
        // Regressão: a recusa do modo jogo era um quadro curto que o parser nunca completava; o pedido expirava
        // e a sessão reconectava, o que o usuário via como "o botão não funciona".
        var channel = new FakeChannel();
        channel.OnWrite = frame => channel.Inject(frame[0]);
        using var link = new HeadsetLink(channel, new HoldUntilFlushParser());
        var closed = false;
        link.Closed += _ => closed = true;

        var reply = await Within(link.RequestAsync(Request(9), TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(3));

        Assert.Equal(new byte[] { 9 }, Assert.IsType<byte[]>(reply));
        Assert.False(closed);
    }

    [Fact]
    public async Task A_write_that_blocks_forever_times_out_and_closes_the_link()
    {
        var channel = new FakeChannel();
        channel.BlockWritesUntilClosed();
        using var link = new HeadsetLink(channel, new EchoParser());
        var closed = new TaskCompletionSource<string>();
        link.Closed += reason => closed.TrySetResult(reason);

        var clock = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Within(link.RequestAsync(Request(1), Timeout), TimeSpan.FromSeconds(5)));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), "Deveria falhar perto do prazo, não esperar a escrita.");
        Assert.Contains("conexão", ex.Message);
        await Within(closed.Task, TimeSpan.FromSeconds(2)); // falha com "travou" se a conexão não foi marcada como perdida
    }

    [Fact]
    public async Task A_request_stuck_on_the_write_does_not_hold_the_queue_forever()
    {
        // Antes da correção, o segundo pedido ficava esperando a vez para sempre.
        var channel = new FakeChannel();
        channel.BlockWritesUntilClosed();
        using var link = new HeadsetLink(channel, new EchoParser());

        var first = link.RequestAsync(Request(1), TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        var second = link.RequestAsync(Request(2), Timeout);

        await Assert.ThrowsAnyAsync<Exception>(() => Within(second, TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<Exception>(() => Within(first, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task No_reply_times_out_without_closing_the_link()
    {
        var channel = new FakeChannel(); // escreve normalmente, mas o fone não responde
        using var link = new HeadsetLink(channel, new EchoParser());
        var closed = false;
        link.Closed += _ => closed = true;

        await Assert.ThrowsAsync<TimeoutException>(() => Within(link.RequestAsync(Request(1), Timeout), TimeSpan.FromSeconds(5)));

        Assert.False(closed, "Uma resposta que não veio não prova que a porta caiu; o fechamento é decisão da sessão.");
    }

    [Fact]
    public async Task The_link_recovers_for_the_next_request_after_a_missing_reply()
    {
        var channel = new FakeChannel();
        using var link = new HeadsetLink(channel, new EchoParser());

        await Assert.ThrowsAsync<TimeoutException>(() => Within(link.RequestAsync(Request(1), Timeout), TimeSpan.FromSeconds(5)));

        channel.OnWrite = frame => channel.Inject(frame[0]);
        var reply = await Within(link.RequestAsync(Request(2), TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(3));

        Assert.Equal(new byte[] { 2 }, Assert.IsType<byte[]>(reply));
    }

    [Fact]
    public async Task A_failed_write_closes_the_link_and_is_reported()
    {
        var channel = new FakeChannel { OnWrite = _ => throw new IOException("sem conexão") };
        using var link = new HeadsetLink(channel, new EchoParser());
        var closed = new TaskCompletionSource<string>();
        link.Closed += reason => closed.TrySetResult(reason);

        await Assert.ThrowsAsync<IOException>(() => Within(link.RequestAsync(Request(1), TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(3)));

        await Within(closed.Task, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Requests_after_the_link_closed_fail_fast()
    {
        var channel = new FakeChannel();
        var link = new HeadsetLink(channel, new EchoParser());
        link.Dispose();

        await Assert.ThrowsAnyAsync<Exception>(() => Within(link.RequestAsync(Request(1), TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Unsolicited_messages_reach_listeners_without_a_pending_request()
    {
        var channel = new FakeChannel();
        using var link = new HeadsetLink(channel, new EchoParser());
        var received = new TaskCompletionSource<object>();
        link.MessageReceived += m => received.TrySetResult(m);

        channel.Inject(42);

        var message = await Within(received.Task, TimeSpan.FromSeconds(2));
        Assert.Equal(new byte[] { 42 }, Assert.IsType<byte[]>(message));
    }
}
