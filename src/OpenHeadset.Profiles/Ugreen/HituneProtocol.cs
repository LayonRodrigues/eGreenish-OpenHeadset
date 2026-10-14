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
    private const int GameModeAt = 6;

    public IMessageParser CreateParser() => new HituneParser();

    public ProtocolRequest ReadState() =>
        new(HituneFrames.Build(HituneCommand.DeviceState, [0]), IsReplyTo(HituneCommand.DeviceState));

    public HeadsetState? DecodeState(object message)
    {
        if (message is not HituneReply { Command: HituneCommand.DeviceState } reply || reply.Payload.Length < StatePayloadLength)
            return null;

        var p = reply.Payload;
        int? battery = p[BatteryAt] <= 100 ? p[BatteryAt] : null;
        return new HeadsetState(battery, DecodeNoise(p[NoiseAt]), p[GameModeAt] != 0);
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
        new(HituneFrames.Build(HituneCommand.GameMode, [(byte)(enabled ? 1 : 0)]), IsReplyTo(HituneCommand.GameMode));

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
}
