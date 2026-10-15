using OpenHeadset.Core;

namespace OpenHeadset.Tests.Profiles;

/// <summary>
/// As regras que todo perfil precisa cumprir, devolvidas como mensagens legíveis.
/// Quem acrescenta um fone novo roda os testes e vê aqui o que está incoerente.
/// </summary>
public static class ProfileContract
{
    public static IReadOnlyList<string> Violations(HeadsetProfile p)
    {
        var problems = new List<string>();
        var caps = p.Capabilities;
        var hasNoise = caps.HasFlag(Capabilities.NoiseControl);

        // Identidade
        if (string.IsNullOrWhiteSpace(p.Id) || p.Id != p.Id.ToLowerInvariant() || p.Id.Contains(' '))
            problems.Add($"Id '{p.Id}' deve ser em minúsculas, sem espaços e não vazio.");
        if (string.IsNullOrWhiteSpace(p.DisplayName))
            problems.Add("DisplayName está vazio.");
        if (p.NameFragments.Count == 0)
            problems.Add("NameFragments está vazio: o perfil nunca reconheceria um fone.");
        foreach (var f in p.NameFragments.Where(f => f.Trim().Length < 6))
            problems.Add($"Trecho de nome '{f}' é curto demais; ele poderia assumir fones que o perfil não conhece.");

        // Modos e níveis
        if (hasNoise && p.NoiseModes.Count == 0)
            problems.Add("Declara NoiseControl mas NoiseModes está vazio.");
        if (!hasNoise && (p.NoiseModes.Count > 0 || p.NoiseLevels.Count > 0))
            problems.Add("Tem NoiseModes/NoiseLevels sem declarar NoiseControl.");
        if (p.NoiseModes.Count != p.NoiseModes.Distinct().Count())
            problems.Add("NoiseModes tem modos repetidos.");
        if (p.NoiseLevels.Count > 0 && !p.NoiseModes.Contains(NoiseMode.Anc))
            problems.Add("Há níveis de ruído, mas o modo Anc não está em NoiseModes.");
        if (p.NoiseLevels.Count != p.NoiseLevels.Select(l => l.Id).Distinct().Count())
            problems.Add("NoiseLevels tem ids repetidos.");
        foreach (var l in p.NoiseLevels.Where(l => l.Id != l.Id.ToLowerInvariant() || string.IsNullOrWhiteSpace(l.Label)))
            problems.Add($"Nível '{l.Id}' precisa de id em minúsculas e de um Label.");

        // Protocolo
        IHeadsetProtocol protocol;
        try
        {
            protocol = p.CreateProtocol();
        }
        catch (Exception e)
        {
            problems.Add($"CreateProtocol lançou {e.GetType().Name}: {e.Message}");
            return problems;
        }

        Guard(problems, "ReadState", () =>
        {
            if (protocol.ReadState().Frame.Length == 0)
                problems.Add("ReadState devolveu um quadro vazio.");
        });

        Guard(problems, "SetNoise", () =>
        {
            var settings = new List<NoiseSetting>(p.NoiseModes.Select(m => new NoiseSetting(m)));
            settings.AddRange(p.NoiseLevels.Select(l => new NoiseSetting(NoiseMode.Anc, l.Id)));
            if (settings.Count == 0)
                settings.Add(new NoiseSetting(NoiseMode.Anc));

            foreach (var s in settings)
            {
                var request = protocol.SetNoise(s, current: null);
                if (hasNoise && (request is null || request.Frame.Length == 0))
                    problems.Add($"Declara NoiseControl mas SetNoise({s.Mode}, {s.LevelId ?? "-"}) não monta um quadro.");
                if (!hasNoise && request is not null)
                    problems.Add("Não declara NoiseControl mas SetNoise devolve um pedido.");
            }
        });

        Guard(problems, "SetGameMode", () =>
        {
            foreach (var enabled in new[] { true, false })
            {
                var request = protocol.SetGameMode(enabled);
                var hasGame = caps.HasFlag(Capabilities.GameMode);
                if (hasGame && (request is null || request.Frame.Length == 0))
                    problems.Add($"Declara GameMode mas SetGameMode({enabled}) não monta um quadro.");
                if (!hasGame && request is not null)
                    problems.Add("Não declara GameMode mas SetGameMode devolve um pedido.");
            }
        });

        Guard(problems, "SetDualLink", () =>
        {
            foreach (var enabled in new[] { true, false })
            {
                var request = protocol.SetDualLink(enabled);
                var hasDual = caps.HasFlag(Capabilities.DualLink);
                if (hasDual && (request is null || request.Frame.Length == 0))
                    problems.Add($"Declara DualLink mas SetDualLink({enabled}) não monta um quadro.");
                if (!hasDual && request is not null)
                    problems.Add("Não declara DualLink mas SetDualLink devolve um pedido.");
            }
        });

        Guard(problems, "SetSpatialAudio", () =>
        {
            foreach (var enabled in new[] { true, false })
            {
                var request = protocol.SetSpatialAudio(enabled);
                var has = caps.HasFlag(Capabilities.SpatialAudio);
                if (has && (request is null || request.Frame.Length == 0))
                    problems.Add($"Declara SpatialAudio mas SetSpatialAudio({enabled}) não monta um quadro.");
                if (!has && request is not null)
                    problems.Add("Não declara SpatialAudio mas SetSpatialAudio devolve um pedido.");
            }
        });

        Guard(problems, "Equalizador", () =>
        {
            var has = caps.HasFlag(Capabilities.Equalizer);
            var presets = p.EqPresets ?? [];
            if (has && presets.Count == 0)
                problems.Add("Declara Equalizer mas não lista nenhum preset em EqPresets.");
            if (!has && presets.Count != 0)
                problems.Add("Não declara Equalizer mas lista presets em EqPresets.");
            if (presets.Select(x => x.Id).Distinct().Count() != presets.Count)
                problems.Add("EqPresets tem ids repetidos.");

            foreach (var preset in presets)
            {
                var request = protocol.SetEqPreset(preset.Id);
                if (request is null || request.Frame.Length == 0)
                    problems.Add($"SetEqPreset({preset.Id}) não monta um quadro para um preset declarado.");
            }
            if (!has && protocol.SetEqPreset(0) is not null)
                problems.Add("Não declara Equalizer mas SetEqPreset devolve um pedido.");
        });

        Guard(problems, "Atalhos dos botões", () =>
        {
            var has = caps.HasFlag(Capabilities.ButtonMapping);
            var gestures = p.ButtonGestures ?? [];
            var functions = p.ButtonFunctions ?? [];
            if (has && (gestures.Count == 0 || functions.Count == 0))
                problems.Add("Declara ButtonMapping mas ButtonGestures ou ButtonFunctions está vazio.");
            if (!has && (gestures.Count != 0 || functions.Count != 0))
                problems.Add("Não declara ButtonMapping mas lista gestos ou funções.");
            if (gestures.Select(x => x.Slot).Distinct().Count() != gestures.Count)
                problems.Add("ButtonGestures tem posições repetidas.");
            if (functions.Select(x => x.Code).Distinct().Count() != functions.Count)
                problems.Add("ButtonFunctions tem códigos repetidos.");

            if (has && gestures.Count > 0 && functions.Count > 0)
            {
                // O estado só é conhecido depois de uma leitura. O contrato não sabe quantos gestos o fone guarda,
                // então tenta os tamanhos que um perfil provavelmente usa; basta um deles produzir um quadro.
                var minimum = gestures.Max(g => g.Slot) + 1;
                var builds = new[] { minimum, 4, 8, 16 }.Where(n => n >= minimum).Distinct().Any(n =>
                {
                    var known = new HeadsetState(50, null, false, Buttons: new int[n]);
                    var request = protocol.SetButton(gestures[0].Slot, functions[0].Code, known);
                    return request is { Frame.Length: > 0 };
                });
                if (!builds)
                    problems.Add("Declara ButtonMapping mas SetButton não monta um quadro com um estado de 4, 8 ou 16 gestos conhecidos.");
            }
        });

        Guard(problems, "SetHighQuality", () =>
        {
            foreach (var enabled in new[] { true, false })
            {
                var request = protocol.SetHighQuality(enabled);
                var hasHq = caps.HasFlag(Capabilities.HighQuality);
                if (hasHq && (request is null || request.Frame.Length == 0))
                    problems.Add($"Declara HighQuality mas SetHighQuality({enabled}) não monta um quadro.");
                if (!hasHq && request is not null)
                    problems.Add("Não declara HighQuality mas SetHighQuality devolve um pedido.");
            }
        });

        Guard(problems, "Parser", () =>
        {
            var parser = protocol.CreateParser();
            if (parser.Feed(ReadOnlySpan<byte>.Empty).Count != 0)
                problems.Add("O parser devolveu mensagens sem receber bytes.");

            var random = new Random(1234);
            var chunk = new byte[64];
            for (var i = 0; i < 2000; i++)
            {
                random.NextBytes(chunk);
                parser.Feed(chunk.AsSpan(0, random.Next(1, chunk.Length)));
            }
            parser.Reset();
        });

        Guard(problems, "Mensagens desconhecidas", () =>
        {
            if (protocol.DecodeState(new object()) is not null)
                problems.Add("DecodeState devolveu um estado para um objeto qualquer; deve devolver nulo.");
            if (protocol.IsStateChangeHint(new object()))
                problems.Add("IsStateChangeHint devolveu verdadeiro para um objeto qualquer.");
        });

        return problems;
    }

    /// <summary>Regras que envolvem o catálogo inteiro.</summary>
    public static IReadOnlyList<string> CatalogViolations(IReadOnlyList<HeadsetProfile> profiles)
    {
        var problems = new List<string>();
        if (profiles.Count == 0)
            problems.Add("O catálogo está vazio.");

        foreach (var dup in profiles.GroupBy(p => p.Id).Where(g => g.Count() > 1))
            problems.Add($"O id '{dup.Key}' aparece em mais de um perfil.");

        foreach (var a in profiles)
        foreach (var fragment in a.NameFragments)
        foreach (var b in profiles.Where(b => b.Id != a.Id && b.Matches(fragment)))
            problems.Add($"O trecho '{fragment}' (perfil {a.Id}) também casa com o perfil {b.Id}.");

        return problems;
    }

    private static void Guard(List<string> problems, string what, Action check)
    {
        try
        {
            check();
        }
        catch (Exception e)
        {
            problems.Add($"{what} lançou {e.GetType().Name}: {e.Message}");
        }
    }
}
