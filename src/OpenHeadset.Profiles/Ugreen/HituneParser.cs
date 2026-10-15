using OpenHeadset.Core;

namespace OpenHeadset.Profiles.Ugreen;

/// <summary>
/// Remonta mensagens a partir de um fluxo de bytes. Tolera mensagens partidas entre leituras,
/// várias por leitura e lixo no meio. O CRC recebido não é verificado
/// (o algoritmo dele ainda não foi reproduzido).
/// </summary>
public sealed class HituneParser : IMessageParser
{
    private const int MaxPayload = 255;

    /// <summary>Cabeçalho (3) + comando + status + 2 bytes finais.</summary>
    private const int ShortReplyLength = 7;

    private readonly List<byte> _buffer = [];

    public IReadOnlyList<object> Feed(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
        var messages = new List<object>();

        while (true)
        {
            var start = IndexOfAnyHeader(0, out var isEvent);
            if (start < 0)
            {
                // Guarda só o que ainda poderia ser o começo de um cabeçalho.
                KeepTail(2);
                break;
            }

            if (start > 0)
                _buffer.RemoveRange(0, start);

            if (isEvent)
            {
                if (_buffer.Count < HituneFrames.EventLength)
                    break;
                messages.Add(new HituneEvent(_buffer[3], _buffer[4], _buffer[5]));
                _buffer.RemoveRange(0, HituneFrames.EventLength);
                continue;
            }

            if (_buffer.Count < 6)
                break; // ainda sem comando/flag/len

            int payloadLen = _buffer[5];
            var total = HituneFrames.ReplyOverhead + payloadLen;
            if (_buffer.Count < total)
            {
                // Sem CRC verificável, um novo cabeçalho dentro de um quadro incompleto
                // indica que o primeiro era lixo: descarta e ressincroniza.
                if (IndexOfAnyHeader(1, out _) >= 0)
                {
                    _buffer.RemoveAt(0);
                    continue;
                }
                break; // quadro incompleto, espera mais bytes
            }

            messages.Add(new HituneReply(_buffer[3], _buffer[4], _buffer.GetRange(6, payloadLen).ToArray()));
            _buffer.RemoveRange(0, total);
        }

        return messages;
    }

    public void Reset() => _buffer.Clear();

    /// <summary>
    /// Quando o fone recusa um comando, responde com um quadro curto: cabeçalho, comando e mais 3 bytes (7 no total),
    /// sem o campo de tamanho que <see cref="Feed"/> espera. Visto no modo jogo (<c>08 00 a6 94</c>, onde o byte de
    /// "tamanho" vale 166) e na ligação dupla (<c>06 01 01 ff</c>), nos dois casos com o Hi-Res ligado. Sem este passo
    /// o quadro ficaria esperando bytes que nunca vêm, o pedido expiraria e a sessão trataria a conexão como perdida.
    /// A mensagem sai com <c>Flag</c> 0, que é como o protocolo representa "recusado".
    /// </summary>
    public IReadOnlyList<object> Flush()
    {
        var messages = new List<object>();
        if (_buffer.Count >= ShortReplyLength && Matches(0, HituneFrames.RxHeader))
            messages.Add(new HituneReply(_buffer[3], Flag: 0, []));

        _buffer.Clear();
        return messages;
    }

    private int IndexOfAnyHeader(int from, out bool isEvent)
    {
        isEvent = false;
        for (var i = from; i + 3 <= _buffer.Count; i++)
        {
            if (Matches(i, HituneFrames.RxHeader))
                return i;
            if (Matches(i, HituneFrames.EventHeader))
            {
                isEvent = true;
                return i;
            }
        }
        return -1;
    }

    private bool Matches(int at, byte[] header) =>
        _buffer[at] == header[0] && _buffer[at + 1] == header[1] && _buffer[at + 2] == header[2];

    private void KeepTail(int count)
    {
        if (_buffer.Count > count)
            _buffer.RemoveRange(0, _buffer.Count - count);
    }
}
