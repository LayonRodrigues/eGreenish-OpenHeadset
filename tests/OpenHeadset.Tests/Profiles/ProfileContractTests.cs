using OpenHeadset.Core;
using OpenHeadset.Profiles;

namespace OpenHeadset.Tests.Profiles;

/// <summary>Aplica o contrato a todo perfil do catálogo e prova que o contrato pega perfis quebrados.</summary>
public class ProfileContractTests
{
    public static IEnumerable<object[]> Profiles() => BuiltInProfiles.All.Select(p => new object[] { p });

    [Theory, MemberData(nameof(Profiles))]
    public void Every_registered_profile_honours_the_contract(HeadsetProfile profile)
    {
        Assert.Empty(ProfileContract.Violations(profile));
    }

    [Fact]
    public void The_catalog_has_unique_ids_and_no_overlapping_names()
    {
        Assert.Empty(ProfileContract.CatalogViolations(BuiltInProfiles.All));
    }

    // ---- O contrato precisa falhar quando deve. Cada perfil abaixo está quebrado de um jeito só. ----

    private sealed class StubProtocol(bool noise = true, bool game = true, bool throwOnParse = false) : IHeadsetProtocol
    {
        private sealed class StubParser(bool throwOnParse) : IMessageParser
        {
            public IReadOnlyList<object> Feed(ReadOnlySpan<byte> data) =>
                throwOnParse && data.Length > 0 ? throw new InvalidOperationException("boom") : [];

            public void Reset() { }
        }

        public IMessageParser CreateParser() => new StubParser(throwOnParse);
        public ProtocolRequest ReadState() => new([1], _ => true);
        public HeadsetState? DecodeState(object message) => null;
        public ProtocolRequest? SetNoise(NoiseSetting desired, HeadsetState? current) => noise ? new ProtocolRequest([2], _ => true) : null;
        public ProtocolRequest? SetGameMode(bool enabled) => game ? new ProtocolRequest([3], _ => true) : null;
        public bool IsStateChangeHint(object message) => false;
    }

    private static HeadsetProfile Good(
        string id = "acme-buds-1",
        IReadOnlyList<string>? fragments = null,
        Capabilities caps = Capabilities.Battery | Capabilities.NoiseControl | Capabilities.GameMode,
        IReadOnlyList<NoiseMode>? modes = null,
        IReadOnlyList<NoiseLevel>? levels = null,
        Func<IHeadsetProtocol>? protocol = null) => new(
            id, "Acme Buds 1", fragments ?? ["Acme Buds 1"], caps,
            modes ?? [NoiseMode.Off, NoiseMode.Anc], levels ?? [], new SerialSettings(),
            protocol ?? (() => new StubProtocol()));

    [Fact]
    public void A_well_formed_profile_passes()
    {
        Assert.Empty(ProfileContract.Violations(Good()));
    }

    [Fact]
    public void Rejects_a_bad_id() =>
        Assert.Contains(ProfileContract.Violations(Good(id: "Acme Buds")), v => v.Contains("Id"));

    [Fact]
    public void Rejects_a_name_fragment_that_is_too_generic() =>
        Assert.Contains(ProfileContract.Violations(Good(fragments: ["Buds"])), v => v.Contains("curto demais"));

    [Fact]
    public void Rejects_a_profile_that_matches_no_name() =>
        Assert.Contains(ProfileContract.Violations(Good(fragments: [])), v => v.Contains("NameFragments"));

    [Fact]
    public void Rejects_noise_control_without_modes() =>
        Assert.Contains(ProfileContract.Violations(Good(modes: [])), v => v.Contains("NoiseModes"));

    [Fact]
    public void Rejects_levels_without_anc() =>
        Assert.Contains(
            ProfileContract.Violations(Good(modes: [NoiseMode.Off], levels: [new NoiseLevel("high", "High")])),
            v => v.Contains("Anc"));

    [Fact]
    public void Rejects_duplicate_level_ids() =>
        Assert.Contains(
            ProfileContract.Violations(Good(levels: [new NoiseLevel("high", "High"), new NoiseLevel("high", "Higher")])),
            v => v.Contains("repetidos"));

    [Fact]
    public void Rejects_a_declared_capability_the_protocol_cannot_build() =>
        Assert.Contains(
            ProfileContract.Violations(Good(protocol: () => new StubProtocol(noise: false))),
            v => v.Contains("Declara NoiseControl"));

    [Fact]
    public void Rejects_a_protocol_that_builds_a_capability_that_was_not_declared() =>
        Assert.Contains(
            ProfileContract.Violations(Good(caps: Capabilities.Battery, modes: [], protocol: () => new StubProtocol())),
            v => v.Contains("Não declara"));

    [Fact]
    public void Rejects_a_parser_that_throws_on_bad_bytes() =>
        Assert.Contains(
            ProfileContract.Violations(Good(protocol: () => new StubProtocol(throwOnParse: true))),
            v => v.Contains("Parser lançou"));

    [Fact]
    public void Catalog_rejects_duplicate_ids()
    {
        var a = Good(id: "same-id", fragments: ["Acme Buds 1"]);
        var b = Good(id: "same-id", fragments: ["Other Model 9"]);
        Assert.Contains(ProfileContract.CatalogViolations([a, b]), v => v.Contains("mais de um perfil"));
    }

    [Fact]
    public void Catalog_rejects_profiles_that_claim_each_others_names()
    {
        var a = Good(id: "acme-1", fragments: ["Acme Buds"]);
        var b = Good(id: "acme-2", fragments: ["Acme Buds Pro"]);
        Assert.Contains(ProfileContract.CatalogViolations([a, b]), v => v.Contains("também casa"));
    }
}
