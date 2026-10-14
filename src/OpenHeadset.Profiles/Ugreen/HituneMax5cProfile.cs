using OpenHeadset.Core;

namespace OpenHeadset.Profiles.Ugreen;

/// <summary>HiTune Max5c. Testado em uma unidade; veja o README para o que foi verificado.</summary>
public static class HituneMax5cProfile
{
    public static HeadsetProfile Create() => new(
        Id: "ugreen-hitune-max5c",
        DisplayName: "HiTune Max5c",
        NameFragments: ["HiTune Max5c"],
        Capabilities: Capabilities.Battery | Capabilities.NoiseControl | Capabilities.GameMode,
        NoiseModes: [NoiseMode.Off, NoiseMode.Anc, NoiseMode.Transparency],
        NoiseLevels:
        [
            new NoiseLevel("deep", "Deep"),
            new NoiseLevel("medium", "Medium"),
            new NoiseLevel("light", "Light"),
            new NoiseLevel("adaptive", "Adaptive"),
        ],
        Serial: new SerialSettings(),
        CreateProtocol: () => new HituneProtocol());
}
