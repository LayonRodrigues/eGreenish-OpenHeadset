using OpenHeadset.Core;

namespace OpenHeadset.Profiles.Ugreen;

/// <summary>Protocolo observado no HiTune Max5c, em texto claro. Ver docs/protocol.md.</summary>
public sealed class HituneProtocol : IHeadsetProtocol
{
    // Nibble alto do byte de ruído -> nível. O modo fica no nibble baixo.
    private static readonly (byte Nibble, string Id)[] Levels =
        [(0xA, "deep"), (0xB, "medium"), (0xC, "light"), (0xD, "adaptive")];

    private const int StatePayloadLength = 22;

    // Posições dentro do payload do estado.
    private const int BatteryAt = 0;
    private const int NoiseAt = 3;
    private const int EqAt = 4;
    private const int DualLinkAt = 5;
    private const int GameModeAt = 6;
    private const int HighQualityAt = 7;
    private const int ButtonsAt = 8;
    private const int SpatialAt = 20;

    /// <summary>Gestos que o fone guarda (toque, duplo, triplo e longo de cada lado), mandados sempre todos juntos.</summary>
    internal const int ButtonSlots = 8;

    // Hi-Res ligado bloqueia modo jogo e ligação dupla (verificado: desligá-lo fez o fone aceitar os dois).
    private const string RefusedByHiRes =
        "O fone recusou. Desligue o áudio Hi-Res: ligado, ele bloqueia o modo jogo e a ligação dupla.";

    public IMessageParser CreateParser() => new HituneParser();

    public ProtocolRequest ReadState() =>
        new(HituneFrames.Build(HituneCommand.DeviceState, [0]), IsReplyTo(HituneCommand.DeviceState));

    public HeadsetState? DecodeState(object message)
    {
        if (message is not HituneReply { Command: HituneCommand.DeviceState } reply || reply.Payload.Length < StatePayloadLength)
            return null;

        var p = reply.Payload;
        int? battery = p[BatteryAt] <= 100 ? p[BatteryAt] : null;
        return new HeadsetState(
            battery,
            DecodeNoise(p[NoiseAt]),
            GameMode: p[GameModeAt] != 0,
            DualLink: p[DualLinkAt] != 0,
            HighQuality: p[HighQualityAt] == 1,
            SpatialAudio: p[SpatialAt] != 0,
            EqPreset: p[EqAt],
            Buttons: p.Skip(ButtonsAt).Take(ButtonSlots).Select(b => (int)b).ToArray());
    }

    public ProtocolRequest? SetNoise(NoiseSetting desired, HeadsetState? current)
    {
        // Ao trocar só o modo, o fone espera que o nível atual seja mantido.
        var levelId = desired.LevelId ?? current?.Noise?.LevelId;
        var nibble = Levels.FirstOrDefault(l => l.Id == levelId).Nibble;
        var value = (byte)((nibble << 4) | ModeBits(desired.Mode));

        return new ProtocolRequest(HituneFrames.Build(HituneCommand.NoiseControl, [value]), IsReplyTo(HituneCommand.NoiseControl));
    }

    public ProtocolRequest? SetGameMode(bool enabled) =>
        new(HituneFrames.Build(HituneCommand.GameMode, [(byte)(enabled ? 1 : 0)]), IsReplyTo(HituneCommand.GameMode),
            WasAccepted, enabled ? RefusedByHiRes : null);

    public ProtocolRequest? SetDualLink(bool enabled) =>
        new(HituneFrames.Build(HituneCommand.DualLink, [(byte)(enabled ? 1 : 0)]), IsReplyTo(HituneCommand.DualLink),
            WasAccepted, enabled ? RefusedByHiRes : null);

    public ProtocolRequest? SetHighQuality(bool enabled) =>
        new(HituneFrames.Build(HituneCommand.HighQuality, [(byte)(enabled ? 1 : 0)]), IsReplyTo(HituneCommand.HighQuality));

    public ProtocolRequest? SetSpatialAudio(bool enabled) =>
        new(HituneFrames.Build(HituneCommand.SpatialAudio, [(byte)(enabled ? 1 : 0)]), IsReplyTo(HituneCommand.SpatialAudio), WasAccepted);

    public ProtocolRequest? SetEqPreset(int presetId) =>
        presetId is < 0 or > byte.MaxValue
            ? null
            : new(HituneFrames.Build(HituneCommand.Equalizer, [(byte)presetId]), IsReplyTo(HituneCommand.Equalizer), WasAccepted);

    /// <summary>O fone recebe as 8 atribuições de uma vez; as outras 7 saem como estão no último estado lido.</summary>
    public ProtocolRequest? SetButton(int slot, int functionCode, HeadsetState? current)
    {
        if (slot is < 0 or >= ButtonSlots || functionCode is < 0 or > byte.MaxValue)
            return null;
        if (current?.Buttons is not { Count: ButtonSlots } known)
            return null;

        var payload = known.Select(b => (byte)b).ToArray();
        payload[slot] = (byte)functionCode;
        return new(HituneFrames.Build(HituneCommand.ButtonSetting, payload), IsReplyTo(HituneCommand.ButtonSetting), WasAccepted);
    }

    public bool IsStateChangeHint(object message) => message is HituneEvent;

    /// <summary>Byte de ruído = <c>(nível &lt;&lt; 4) | modo</c>.</summary>
    internal static NoiseSetting DecodeNoise(byte raw)
    {
        var mode = (raw & 0x0F) switch
        {
            1 => NoiseMode.Anc,
            2 => NoiseMode.Transparency,
            _ => NoiseMode.Off,
        };
        var nibble = (byte)(raw >> 4);
        var level = Levels.Where(l => l.Nibble == nibble).Select(l => l.Id).FirstOrDefault();
        return new NoiseSetting(mode, level);
    }

    internal static byte ModeBits(NoiseMode mode) => mode switch
    {
        NoiseMode.Anc => 1,
        NoiseMode.Transparency => 2,
        _ => 0,
    };

    private static Func<object, bool> IsReplyTo(byte command) =>
        message => message is HituneReply reply && reply.Command == command;

    /// <summary>Flag 0 na resposta é recusa do fone (quadro curto, ver <see cref="HituneParser.Flush"/>); as confirmações trazem 1.</summary>
    private static bool WasAccepted(object message) => message is HituneReply { Flag: not 0 };
}
