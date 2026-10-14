namespace OpenHeadset.Profiles.Ugreen;

/// <summary>Comandos observados (ver docs/protocol.md).</summary>
internal static class HituneCommand
{
    public const byte Version = 1;
    public const byte DeviceState = 4;
    public const byte GameMode = 8;
    public const byte NoiseControl = 9;
}

/// <summary>Resposta a um comando (cabeçalho DD EE FF).</summary>
public sealed record HituneReply(byte Command, byte Flag, byte[] Payload);

/// <summary>Aviso espontâneo do fone (cabeçalho 85 86 87, 6 bytes). O significado de cada campo foi observado só em parte.</summary>
public sealed record HituneEvent(byte Type, byte Sub, byte Value);

/// <summary>Montagem de quadros enviados ao fone.</summary>
public static class HituneFrames
{
    /// <summary>Início de todo quadro enviado ao fone.</summary>
    public static readonly byte[] TxHeader = [0xAA, 0xBB, 0xCC];

    /// <summary>Início de toda resposta recebida do fone.</summary>
    public static readonly byte[] RxHeader = [0xDD, 0xEE, 0xFF];

    /// <summary>Início de um aviso espontâneo.</summary>
    public static readonly byte[] EventHeader = [0x85, 0x86, 0x87];

    /// <summary>Bytes fixos de uma resposta além do payload: cabeçalho(3) + cmd + flag + len + crc(2).</summary>
    public const int ReplyOverhead = 8;

    public const int EventLength = 6;

    /// <summary>CRC-16/MODBUS: polinômio refletido 0xA001, valor inicial 0xFFFF.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>Monta <c>AA BB CC | cmd | len | payload | crcLo | crcHi</c>. O CRC cobre cmd+len+payload.</summary>
    public static byte[] Build(byte command, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > byte.MaxValue)
            throw new ArgumentException("Payload maior que 255 bytes.", nameof(payload));

        var frame = new byte[TxHeader.Length + 2 + payload.Length + 2];
        TxHeader.CopyTo(frame, 0);
        frame[3] = command;
        frame[4] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(5));

        var crc = Crc16(frame.AsSpan(3, 2 + payload.Length));
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }
}
