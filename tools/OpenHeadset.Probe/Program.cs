using System.Diagnostics;
using System.IO.Ports;
using OpenHeadset.Transport;

// Ferramenta de captura para quem quer suportar um fone novo. Não conhece protocolo nenhum:
// só lista portas, escuta e envia bytes que VOCÊ escolheu, mostrando tudo em hexadecimal.
//
//   probe list
//   probe listen <COMn> [segundos]
//   probe send   <COMn> <bytes em hex...> [--wait segundos]
//
// Regra de ouro: envie apenas bytes que você viu o app do próprio fabricante enviar.

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "list" => List(),
        "listen" when args.Length >= 2 => Listen(args[1], args.Length > 2 ? ParseSeconds(args[2]) : 30),
        "send" when args.Length >= 3 => Send(args[1], args[2..]),
        _ => Fail("Comando inválido."),
    };
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException)
{
    Console.Error.WriteLine($"Erro: {e.Message}");
    return 1;
}

static int List()
{
    var ports = HeadsetLocator.ListPairedSerialPorts();
    if (ports.Count == 0)
    {
        Console.WriteLine("Nenhuma porta serial Bluetooth encontrada. Pareie o fone nas configurações do Windows.");
        Console.WriteLine("Se o fone não cria porta serial, ele provavelmente usa só BLE; veja docs/adding-a-headset.md.");
        return 0;
    }

    Console.WriteLine($"{"Dispositivo",-34} {"Endereço",-14} {"Porta",-7} Serviço");
    foreach (var p in ports.OrderBy(p => p.DeviceName))
        Console.WriteLine($"{p.DeviceName,-34} {p.Address,-14} {p.PortName,-7} {p.ServiceUuid}");
    return 0;
}

static int Listen(string portName, int seconds)
{
    using var port = Open(portName);
    Console.WriteLine($"Escutando {portName} por {seconds} s. Mexa nos controles do fone (botões, outro app) e observe.");
    var clock = Stopwatch.StartNew();
    ReadFor(port, seconds, clock);
    return 0;
}

static int Send(string portName, string[] rest)
{
    var wait = 3;
    var hexParts = new List<string>();
    for (var i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "--wait" && i + 1 < rest.Length)
            wait = ParseSeconds(rest[++i]);
        else
            hexParts.Add(rest[i]);
    }

    var bytes = Convert.FromHexString(string.Concat(hexParts).Replace("-", "").Replace(":", ""));
    using var port = Open(portName);
    var clock = Stopwatch.StartNew();

    port.Write(bytes, 0, bytes.Length);
    Console.WriteLine($"[{clock.Elapsed.TotalSeconds,7:F3}s] TX {bytes.Length,3}B  {Format(bytes)}");
    ReadFor(port, wait, clock);
    return 0;
}

static SerialPort Open(string portName)
{
    var port = new SerialPort(portName, 115200) { ReadTimeout = 200, WriteTimeout = 3000, DtrEnable = true, RtsEnable = true };
    port.Open();
    return port;
}

static void ReadFor(SerialPort port, int seconds, Stopwatch clock)
{
    var end = clock.Elapsed + TimeSpan.FromSeconds(seconds);
    var buffer = new byte[512];
    while (clock.Elapsed < end)
    {
        try
        {
            var n = port.Read(buffer, 0, buffer.Length);
            if (n > 0)
                Console.WriteLine($"[{clock.Elapsed.TotalSeconds,7:F3}s] RX {n,3}B  {Format(buffer.AsSpan(0, n))}");
        }
        catch (TimeoutException)
        {
            // sem dados neste intervalo
        }
    }
}

static string Format(ReadOnlySpan<byte> data) => string.Join(' ', data.ToArray().Select(b => b.ToString("x2")));

static int ParseSeconds(string text) =>
    int.TryParse(text, out var s) && s is > 0 and <= 3600 ? s : throw new FormatException($"Segundos inválidos: {text}");

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    PrintUsage();
    return 2;
}

static void PrintUsage() => Console.WriteLine(
    """
    OpenHeadset.Probe — captura de protocolo (só envia o que você mandar)

      probe list                                  portas seriais Bluetooth dos fones pareados
      probe listen <COMn> [segundos]              escuta sem enviar nada (padrão: 30 s)
      probe send <COMn> <hex...> [--wait N]       envia bytes e mostra a resposta (padrão: 3 s)

    Exemplo:  probe send COM5 aa bb cc 04 01 00 31 91 --wait 2

    Envie somente bytes que você viu o app oficial do fabricante enviar ao seu próprio fone.
    Nada de atualização de firmware, restauração de fábrica ou tentativa e erro.
    """);
