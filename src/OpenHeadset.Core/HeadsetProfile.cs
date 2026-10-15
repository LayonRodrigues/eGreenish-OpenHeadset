namespace OpenHeadset.Core;

/// <summary>Como falar com o fone pela porta serial Bluetooth (SPP) que o Windows cria para ele.</summary>
/// <param name="ServiceUuid">UUID do serviço serial. O padrão (0x1101) é o do Serial Port Profile.</param>
/// <param name="BaudRate">Taxa da porta; em Bluetooth costuma não fazer diferença, mas alguns fones esperam um valor.</param>
public sealed record SerialSettings(
    string ServiceUuid = "00001101-0000-1000-8000-00805F9B34FB",
    int BaudRate = 115200);

/// <summary>
/// Tudo o que o aplicativo precisa saber sobre um modelo de fone. Para suportar um fone novo basta criar
/// um perfil e registrá-lo no catálogo; veja docs/adding-a-headset.md.
/// </summary>
/// <param name="Id">Identificador único e estável, em minúsculas (ex.: "vendor-model").</param>
/// <param name="DisplayName">Nome para mostrar.</param>
/// <param name="NameFragments">
/// Trechos do nome Bluetooth do fone (sem diferenciar maiúsculas) que identificam este modelo.
/// Seja específico: um trecho genérico demais faria o perfil assumir fones que ele não conhece.
/// </param>
/// <param name="Capabilities">Controles que este fone suporta e foram verificados.</param>
/// <param name="NoiseModes">Modos de ruído que o fone tem, em ordem de exibição. Vazio se não há controle de ruído.</param>
/// <param name="NoiseLevels">Níveis de cancelamento, em ordem de exibição. Vazio se o fone não tem níveis.</param>
/// <param name="Serial">Parâmetros da porta serial.</param>
/// <param name="CreateProtocol">Cria o protocolo (uma instância por conexão).</param>
/// <param name="EqPresets">Presets de equalizador, em ordem de exibição. Só vale com <see cref="Capabilities.Equalizer"/>.</param>
/// <param name="ButtonGestures">Gestos configuráveis, em ordem de exibição. Só vale com <see cref="Capabilities.ButtonMapping"/>.</param>
/// <param name="ButtonFunctions">Funções que se pode atribuir a um gesto. Só vale com <see cref="Capabilities.ButtonMapping"/>.</param>
public sealed record HeadsetProfile(
    string Id,
    string DisplayName,
    IReadOnlyList<string> NameFragments,
    Capabilities Capabilities,
    IReadOnlyList<NoiseMode> NoiseModes,
    IReadOnlyList<NoiseLevel> NoiseLevels,
    SerialSettings Serial,
    Func<IHeadsetProtocol> CreateProtocol,
    IReadOnlyList<EqPreset>? EqPresets = null,
    IReadOnlyList<ButtonGesture>? ButtonGestures = null,
    IReadOnlyList<ButtonFunction>? ButtonFunctions = null)
{
    public bool Matches(string bluetoothName) =>
        NameFragments.Any(f => bluetoothName.Contains(f, StringComparison.OrdinalIgnoreCase));
}
