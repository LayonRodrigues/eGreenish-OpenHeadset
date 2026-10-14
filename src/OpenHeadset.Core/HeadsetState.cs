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
public sealed record HeadsetState(
    int? BatteryPercent,
    NoiseSetting? Noise,
    bool? GameMode);

/// <summary>O que um perfil sabe fazer. A interface mostra só os controles declarados aqui.</summary>
[Flags]
public enum Capabilities
{
    None = 0,
    Battery = 1,
    NoiseControl = 2,
    GameMode = 4,
}
