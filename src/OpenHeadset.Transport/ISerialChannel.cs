using System.IO.Ports;

namespace OpenHeadset.Transport;

/// <summary>
/// O que o <see cref="HeadsetLink"/> precisa de uma porta serial. Existe para que o comportamento diante de uma
/// porta que trava ou some possa ser testado sem um fone de verdade.
/// </summary>
internal interface ISerialChannel : IDisposable
{
    /// <summary>Lê bytes, bloqueando. Lança <see cref="TimeoutException"/> se nada chegar no intervalo de leitura.</summary>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>Escreve bytes, bloqueando. Numa porta Bluetooth cujo fone sumiu, isto pode demorar ou nunca voltar.</summary>
    void Write(byte[] buffer, int offset, int count);
}

internal sealed class SerialPortChannel : ISerialChannel
{
    private readonly SerialPort _port;

    private SerialPortChannel(SerialPort port) => _port = port;

    public static SerialPortChannel Open(string portName, int baudRate)
    {
        var port = new SerialPort(portName, baudRate)
        {
            ReadTimeout = 500,
            WriteTimeout = 3000,
            DtrEnable = true,
            RtsEnable = true,
        };
        port.Open();
        return new SerialPortChannel(port);
    }

    public int Read(byte[] buffer, int offset, int count) => _port.BaseStream.Read(buffer, offset, count);

    public void Write(byte[] buffer, int offset, int count) => _port.BaseStream.Write(buffer, offset, count);

    public void Dispose()
    {
        try { _port.Close(); } catch { /* já caiu */ }
        try { _port.Dispose(); } catch { /* idem */ }
    }
}
