namespace OpenHeadset.Core;

/// <summary>O que o fone está fazendo com o ruído externo.</summary>
public enum NoiseMode
{
    Off,
    Anc,
    Transparency,
}

/// <summary>
/// Um nível de cancelamento (por exemplo "profundo" ou "adaptativo"). Cada perfil declara os seus:
/// o número e os nomes dos níveis variam de fone para fone.
/// </summary>
/// <param name="Id">Identificador estável, em minúsculas (ex.: "deep"). É o que o código compara.</param>
/// <param name="Label">Texto mostrado ao usuário.</param>
public sealed record NoiseLevel(string Id, string Label);

/// <param name="Mode">Modo de ruído.</param>
/// <param name="LevelId">Nível escolhido (<see cref="NoiseLevel.Id"/>); nulo quando não se aplica ou não é conhecido.</param>
public sealed record NoiseSetting(NoiseMode Mode, string? LevelId = null);

/// <summary>
/// Estado lido do fone, sem nada específico de marca. Um campo nulo significa "este fone não informa isso".
/// </summary>
/// <param name="EqPreset"><see cref="EqPreset.Id"/> do equalizador ativo.</param>
/// <param name="Buttons">Função atribuída a cada gesto, na ordem dos <see cref="ButtonGesture.Slot"/>. Os códigos são do fone.</param>
public sealed record HeadsetState(
    int? BatteryPercent,
    NoiseSetting? Noise,
    bool? GameMode,
    bool? DualLink = null,
    bool? HighQuality = null,
    bool? SpatialAudio = null,
    int? EqPreset = null,
    IReadOnlyList<int>? Buttons = null);

/// <summary>Um preset de equalizador. <paramref name="Id"/> é o valor que o fone usa.</summary>
public sealed record EqPreset(int Id, string Label);

/// <summary>Um gesto configurável (por exemplo, "toque duplo"). <paramref name="Slot"/> é a posição dele em <see cref="HeadsetState.Buttons"/>.</summary>
public sealed record ButtonGesture(int Slot, string Label);

/// <summary>Uma função que se pode atribuir a um gesto. <paramref name="Code"/> é o valor que o fone usa.</summary>
public sealed record ButtonFunction(int Code, string Label);

/// <summary>O que um perfil sabe fazer. A interface mostra só os controles declarados aqui.</summary>
[Flags]
public enum Capabilities
{
    None = 0,
    Battery = 1,
    NoiseControl = 2,
    GameMode = 4,

    /// <summary>Ligação a dois aparelhos ao mesmo tempo.</summary>
    DualLink = 8,

    /// <summary>Decodificação de áudio de alta qualidade (Hi-Res). Neste fone, ligada, bloqueia modo jogo e ligação dupla.</summary>
    HighQuality = 16,

    Equalizer = 32,
    SpatialAudio = 64,

    /// <summary>Atribuir uma função a cada gesto de toque.</summary>
    ButtonMapping = 128,
}
