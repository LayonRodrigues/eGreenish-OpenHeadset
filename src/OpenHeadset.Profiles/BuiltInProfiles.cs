using OpenHeadset.Core;
using OpenHeadset.Profiles.Ugreen;

namespace OpenHeadset.Profiles;

/// <summary>
/// Catálogo de fones suportados. Para incluir um fone novo, crie o perfil dele numa pasta própria
/// (uma pasta por fabricante) e acrescente uma linha aqui. Veja docs/adding-a-headset.md.
/// </summary>
public static class BuiltInProfiles
{
    public static IReadOnlyList<HeadsetProfile> All { get; } =
    [
        HituneMax5cProfile.Create(),
    ];
}
