using System.Management;
using System.Text.RegularExpressions;
using OpenHeadset.Core;

namespace OpenHeadset.Transport;

/// <summary>Um fone pareado, o perfil que o reconhece e a porta serial que o Windows criou para ele.</summary>
/// <param name="Name">Nome Bluetooth do fone, como o Windows o mostra.</param>
/// <param name="Address">Endereço Bluetooth (12 dígitos hexadecimais).</param>
/// <param name="PortName">Porta serial do serviço do perfil (ex.: COM3).</param>
public sealed record DetectedHeadset(HeadsetProfile Profile, string Name, string Address, string PortName);

/// <summary>Uma porta serial Bluetooth de um dispositivo pareado. Usada pela ferramenta de captura.</summary>
public sealed record PairedSerialPort(string DeviceName, string Address, string PortName, string ServiceUuid);

/// <summary>
/// Acha o fone pareado e a porta serial dele pelos IDs do Windows. Não usa o texto localizado
/// do nome da porta e não assume o número COM, que muda de PC para PC.
/// </summary>
public static partial class HeadsetLocator
{
    /// <summary>Procura o primeiro fone pareado que algum dos perfis reconhece.</summary>
    public static DetectedHeadset? Find(IEnumerable<HeadsetProfile> profiles)
    {
        var devices = Query(@"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTHENUM\\DEV[_]%'");
        var portsByUuid = new Dictionary<string, List<(string Name, string Id)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, id) in devices)
        {
            var profile = profiles.FirstOrDefault(p => p.Matches(name));
            if (profile is null)
                continue;

            var address = DeviceAddress().Match(id).Groups[1].Value;
            if (address.Length == 0)
                continue;

            var uuid = profile.Serial.ServiceUuid;
            if (!portsByUuid.TryGetValue(uuid, out var ports))
                portsByUuid[uuid] = ports = Query($@"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTHENUM\\{{{uuid}}}%'");

            foreach (var (portLabel, portId) in ports)
            {
                if (!portId.Contains(address, StringComparison.OrdinalIgnoreCase))
                    continue;

                var com = ComPort().Match(portLabel).Groups[1].Value;
                if (com.Length > 0)
                    return new DetectedHeadset(profile, name, address, com);
            }
        }

        return null;
    }

    /// <summary>Lista todas as portas seriais Bluetooth de dispositivos pareados, sem filtrar por perfil.</summary>
    public static IReadOnlyList<PairedSerialPort> ListPairedSerialPorts()
    {
        var names = Query(@"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTHENUM\\DEV[_]%'")
            .Select(d => (Address: DeviceAddress().Match(d.Id).Groups[1].Value, d.Name))
            .Where(d => d.Address.Length > 0)
            .ToDictionary(d => d.Address.ToUpperInvariant(), d => d.Name);

        var result = new List<PairedSerialPort>();
        foreach (var (label, id) in Query(@"SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'BTHENUM\\{%'"))
        {
            var com = ComPort().Match(label).Groups[1].Value;
            var uuid = ServiceUuidInId().Match(id).Groups[1].Value;
            var address = PortAddress().Match(id).Groups[1].Value.ToUpperInvariant();
            if (com.Length == 0 || uuid.Length == 0 || address.Length == 0)
                continue;

            result.Add(new PairedSerialPort(names.GetValueOrDefault(address, "(sem nome)"), address, com, uuid));
        }
        return result;
    }

    private static List<(string Name, string Id)> Query(string wql)
    {
        var result = new List<(string, string)>();
        using var searcher = new ManagementObjectSearcher(wql);
        foreach (ManagementBaseObject o in searcher.Get())
        {
            var name = o["Name"] as string;
            var id = o["PNPDeviceID"] as string;
            if (name is not null && id is not null)
                result.Add((name, id));
        }
        return result;
    }

    [GeneratedRegex(@"DEV_([0-9A-Fa-f]{12})")]
    private static partial Regex DeviceAddress();

    [GeneratedRegex(@"\((COM\d+)\)")]
    private static partial Regex ComPort();

    [GeneratedRegex(@"\{([0-9A-Fa-f\-]{36})\}")]
    private static partial Regex ServiceUuidInId();

    [GeneratedRegex(@"&([0-9A-Fa-f]{12})_C")]
    private static partial Regex PortAddress();
}
