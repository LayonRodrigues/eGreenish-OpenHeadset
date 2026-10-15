using OpenHeadset.Core;

namespace OpenHeadset.Profiles.Ugreen;

/// <summary>HiTune Max5c. Testado em uma unidade; veja o README para o que foi verificado.</summary>
public static class HituneMax5cProfile
{
    public static HeadsetProfile Create() => new(
        Id: "ugreen-hitune-max5c",
        DisplayName: "HiTune Max5c",
        NameFragments: ["HiTune Max5c"],
        Capabilities: Capabilities.Battery | Capabilities.NoiseControl | Capabilities.GameMode | Capabilities.DualLink
                      | Capabilities.HighQuality | Capabilities.SpatialAudio | Capabilities.Equalizer | Capabilities.ButtonMapping,
        NoiseModes: [NoiseMode.Off, NoiseMode.Anc, NoiseMode.Transparency],
        NoiseLevels:
        [
            new NoiseLevel("deep", "Deep"),
            new NoiseLevel("medium", "Medium"),
            new NoiseLevel("light", "Light"),
            new NoiseLevel("adaptive", "Adaptive"),
        ],
        Serial: new SerialSettings(),
        CreateProtocol: () => new HituneProtocol(),
        // Nomes e números do enum de presets do app do fabricante. O fone aceitou 0 a 12 e devolveu o valor na leitura;
        // o 12 (personalizado) fica de fora porque precisa das bandas do equalizador, que este app não edita.
        EqPresets:
        [
            new EqPreset(0, "Clássico"), new EqPreset(1, "Jazz"), new EqPreset(2, "Eletrônico"), new EqPreset(3, "Pop"),
            new EqPreset(4, "Clássica"), new EqPreset(5, "Rock"), new EqPreset(6, "Graves"), new EqPreset(7, "Agudos"),
            new EqPreset(8, "Vocal"), new EqPreset(9, "Cinema"), new EqPreset(10, "Jogos"), new EqPreset(11, "Podcast"),
        ],
        // O fone guarda 8 gestos (4 por lado). Nesta unidade os 4 últimos vieram zerados, então só os 4 primeiros
        // aparecem; os outros 4 ficam como estão.
        ButtonGestures:
        [
            new ButtonGesture(0, "Toque"), new ButtonGesture(1, "Toque duplo"),
            new ButtonGesture(2, "Toque triplo"), new ButtonGesture(3, "Toque longo"),
        ],
        // Códigos do app do fabricante. Só o 7 (play/pause) foi testado no fone; o fone também usa códigos que
        // não estão aqui (o toque longo veio de fábrica com 11), e a interface os mostra como "Outra (n)".
        ButtonFunctions:
        [
            new ButtonFunction(0, "Nenhuma"), new ButtonFunction(1, "Atender / desligar"),
            new ButtonFunction(2, "Assistente de voz"), new ButtonFunction(3, "Faixa anterior"),
            new ButtonFunction(4, "Próxima faixa"), new ButtonFunction(5, "Volume +"), new ButtonFunction(6, "Volume −"),
            new ButtonFunction(7, "Play / pause"), new ButtonFunction(8, "Modo jogo"), new ButtonFunction(9, "Controle de ruído"),
        ]);
}
