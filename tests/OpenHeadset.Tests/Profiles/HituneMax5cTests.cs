using OpenHeadset.Core;
using OpenHeadset.Profiles.Ugreen;

namespace OpenHeadset.Tests.Profiles;

/// <summary>Os vetores abaixo são bytes reais capturados de um fone, salvo onde indicado.</summary>
public class HituneMax5cTests
{
    private static readonly HituneProtocol Protocol = new();

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    private static List<HituneReply> Replies(params byte[][] chunks)
    {
        var parser = new HituneParser();
        return chunks.SelectMany(c => parser.Feed(c)).OfType<HituneReply>().ToList();
    }

    private const string StateFrame =
        "dd ee ff 04 01 16 5a ff ff a0 00 00 00 00 08 00 00 07 00 00 00 00 00 00 00 09 00 00 bc bd";

    // ---------- Quadros enviados ----------

    [Theory]
    [InlineData(0x09, "a0", "aa bb cc 09 01 a0 a0 2a")]
    [InlineData(0x09, "a2", "aa bb cc 09 01 a2 21 eb")]
    [InlineData(0x04, "00", "aa bb cc 04 01 00 31 91")]
    [InlineData(0x01, "00", "aa bb cc 01 01 00 21 90")]
    public void Build_matches_frames_the_headset_accepted(byte cmd, string payload, string expected)
    {
        Assert.Equal(Hex(expected), HituneFrames.Build(cmd, Hex(payload)));
    }

    [Fact]
    public void ReadState_builds_the_captured_request()
    {
        Assert.Equal(Hex("aa bb cc 04 01 00 31 91"), Protocol.ReadState().Frame);
    }

    [Fact]
    public void SetNoise_off_keeps_the_current_level_like_the_original_app()
    {
        var current = new HeadsetState(90, new NoiseSetting(NoiseMode.Anc, "deep"), false);
        var request = Protocol.SetNoise(new NoiseSetting(NoiseMode.Off), current);
        Assert.Equal(Hex("aa bb cc 09 01 a0 a0 2a"), request!.Frame);
    }

    [Fact]
    public void SetNoise_transparency_matches_the_captured_frame()
    {
        var current = new HeadsetState(90, new NoiseSetting(NoiseMode.Off, "deep"), false);
        var request = Protocol.SetNoise(new NoiseSetting(NoiseMode.Transparency), current);
        Assert.Equal(Hex("aa bb cc 09 01 a2 21 eb"), request!.Frame);
    }

    [Theory]
    [InlineData("deep", 0xA1)]
    [InlineData("medium", 0xB1)]
    [InlineData("light", 0xC1)]
    [InlineData("adaptive", 0xD1)]
    public void SetNoise_anc_encodes_each_level(string level, byte expected)
    {
        var request = Protocol.SetNoise(new NoiseSetting(NoiseMode.Anc, level), current: null);
        Assert.Equal(HituneFrames.Build(HituneCommand.NoiseControl, [expected]), request!.Frame);
    }

    [Fact]
    public void SetGameMode_builds_both_states()
    {
        Assert.Equal(HituneFrames.Build(HituneCommand.GameMode, [1]), Protocol.SetGameMode(true)!.Frame);
        Assert.Equal(HituneFrames.Build(HituneCommand.GameMode, [0]), Protocol.SetGameMode(false)!.Frame);
    }

    [Fact]
    public void SetDualLink_builds_frames_captured_on_a_real_unit()
    {
        // Enviados ao fone real em 2026-10-15: o celular passou a conectar enquanto o PC seguia conectado.
        Assert.Equal(Hex("aa bb cc 06 01 01 51 91"), Protocol.SetDualLink(true)!.Frame);
        Assert.Equal(Hex("aa bb cc 06 01 00 90 51"), Protocol.SetDualLink(false)!.Frame);
    }

    // ---------- Quadros recebidos ----------

    [Fact]
    public void A_short_rejection_is_recovered_by_flush_and_marked_as_not_accepted()
    {
        // Captura real, com o Hi-Res ligado: o fone recusou o modo jogo. O "tamanho" (a6) não descreve o quadro de 7 bytes.
        var parser = new HituneParser();
        Assert.Empty(parser.Feed(Hex("dd ee ff 08 00 a6 94")));

        var reply = Assert.IsType<HituneReply>(Assert.Single(parser.Flush()));

        var request = Protocol.SetGameMode(true)!;
        Assert.True(request.IsReply(reply));
        Assert.False(request.IsAccepted!(reply));
    }

    [Fact]
    public void A_short_dual_link_reply_is_a_refusal_with_the_reason_for_the_user()
    {
        // Captura real, com o Hi-Res ligado: o fone recusou a ligação dupla com este quadro curto.
        var parser = new HituneParser();
        Assert.Empty(parser.Feed(Hex("dd ee ff 06 01 01 ff")));

        var reply = Assert.IsType<HituneReply>(Assert.Single(parser.Flush()));

        var request = Protocol.SetDualLink(true)!;
        Assert.True(request.IsReply(reply));
        Assert.False(request.IsAccepted!(reply));
        Assert.Contains("Hi-Res", request.RefusedMessage);
    }

    [Theory]
    [InlineData("dd ee ff 06 01 01 01 79 b7", true)]  // ligar, com o Hi-Res desligado: eco normal
    [InlineData("dd ee ff 06 01 01 00 58 a7", false)] // desligar: eco normal
    public void A_normal_dual_link_echo_is_accepted(string frame, bool enabled)
    {
        var reply = Assert.Single(Replies(Hex(frame)));

        Assert.True(Protocol.SetDualLink(enabled)!.IsAccepted!(reply));
    }

    [Fact]
    public void Turning_a_feature_off_never_blames_hi_res()
    {
        Assert.Null(Protocol.SetGameMode(false)!.RefusedMessage);
        Assert.Null(Protocol.SetDualLink(false)!.RefusedMessage);
    }

    [Fact]
    public void SetHighQuality_builds_the_captured_frames()
    {
        Assert.Equal(Hex("aa bb cc 0b 01 00 01 92"), Protocol.SetHighQuality(false)!.Frame);
        Assert.Equal(Hex("aa bb cc 0b 01 01 c0 52"), Protocol.SetHighQuality(true)!.Frame);
    }

    [Fact]
    public void SetSpatialAudio_and_SetEqPreset_build_the_frames_the_headset_echoed()
    {
        // Enviados ao fone real em 2026-10-15; o fone devolveu o valor e o estado o refletiu (bytes 20 e 4).
        Assert.Equal(Hex("aa bb cc 12 01 01 11 95"), Protocol.SetSpatialAudio(true)!.Frame);
        Assert.Equal(Hex("aa bb cc 12 01 00 d0 55"), Protocol.SetSpatialAudio(false)!.Frame);
        Assert.Equal(Hex("aa bb cc 05 01 03 20 50"), Protocol.SetEqPreset(3)!.Frame);
        Assert.Equal(Hex("aa bb cc 05 01 00 60 51"), Protocol.SetEqPreset(0)!.Frame);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void SetEqPreset_rejects_a_value_that_does_not_fit_in_a_byte(int preset)
    {
        Assert.Null(Protocol.SetEqPreset(preset));
    }

    [Fact]
    public void SetButton_sends_all_eight_gestures_and_changes_only_the_chosen_one()
    {
        // Captura real: o toque triplo (posição 2) passou de 0 a 7 (play/pause); as outras sete saíram como estavam.
        var current = new HeadsetState(40, null, false, Buttons: [0x08, 0x07, 0x00, 0x0b, 0, 0, 0, 0]);

        var request = Protocol.SetButton(slot: 2, functionCode: 7, current)!;

        Assert.Equal(Hex("aa bb cc 0a 08 08 07 07 0b 00 00 00 00 e4 f7"), request.Frame);
    }

    [Fact]
    public void SetButton_refuses_to_guess_the_other_gestures()
    {
        // Sem o estado atual, mandar 8 bytes zeraria os outros gestos do usuário.
        Assert.Null(Protocol.SetButton(2, 7, current: null));
        Assert.Null(Protocol.SetButton(2, 7, new HeadsetState(40, null, false)));
        Assert.Null(Protocol.SetButton(2, 7, new HeadsetState(40, null, false, Buttons: [1, 2, 3])));
        Assert.Null(Protocol.SetButton(8, 7, new HeadsetState(40, null, false, Buttons: new int[8])));
        Assert.Null(Protocol.SetButton(0, 256, new HeadsetState(40, null, false, Buttons: new int[8])));
    }

    [Fact]
    public void DecodeState_reads_equalizer_spatial_audio_and_the_eight_gestures()
    {
        // Captura real (o código 0x0b no toque longo veio assim no fone e não consta da tabela de funções conhecidas).
        var reply = Assert.Single(Replies(Hex("dd ee ff 04 01 16 28 ff ff a0 00 01 01 00 08 07 00 0b 00 00 00 00 00 00 00 09 00 00 ba e5")));
        var state = Protocol.DecodeState(reply)!;

        Assert.Equal(0, state.EqPreset);
        Assert.False(state.SpatialAudio);
        Assert.Equal(new[] { 8, 7, 0, 11, 0, 0, 0, 0 }, state.Buttons);
    }

    [Fact]
    public void DecodeState_reads_hi_res_on_with_game_mode_and_dual_link_off()
    {
        // Captura real: fone com o Hi-Res ligado, que é quando ele recusava modo jogo e ligação dupla.
        var reply = Assert.Single(Replies(Hex("dd ee ff 04 01 16 32 ff ff a1 00 00 00 01 08 07 00 0b 00 00 00 00 00 00 00 09 01 00 fb 26")));
        var state = Protocol.DecodeState(reply)!;

        Assert.True(state.HighQuality);
        Assert.False(state.GameMode);
        Assert.False(state.DualLink);
        Assert.Equal(50, state.BatteryPercent);
    }

    [Fact]
    public void DecodeState_reads_game_mode_and_dual_link_on_with_hi_res_off()
    {
        // Captura real, depois de desligar o Hi-Res e ligar o modo jogo e a ligação dupla.
        var reply = Assert.Single(Replies(Hex("dd ee ff 04 01 16 28 ff ff a0 00 01 01 00 08 07 00 0b 00 00 00 00 00 00 00 09 00 00 ba e5")));
        var state = Protocol.DecodeState(reply)!;

        Assert.False(state.HighQuality);
        Assert.True(state.GameMode);
        Assert.True(state.DualLink);
        Assert.Equal(40, state.BatteryPercent);
    }

    [Fact]
    public void Flush_discards_leftovers_that_are_not_a_short_frame_and_does_not_hold_the_next_frame()
    {
        var parser = new HituneParser();
        parser.Feed(Hex("dd ee ff 08 00")); // incompleto demais para ser um quadro curto
        Assert.Empty(parser.Flush());

        var next = Assert.Single(parser.Feed(Hex("dd ee ff 09 01 01 a0 5c c6")).OfType<HituneReply>());
        Assert.Equal(HituneCommand.NoiseControl, next.Command);
    }

    [Fact]
    public void Parser_reads_a_device_state_frame()
    {
        var parsed = Assert.Single(Replies(Hex(StateFrame)));
        Assert.Equal(HituneCommand.DeviceState, parsed.Command);
        Assert.Equal(22, parsed.Payload.Length);
    }

    [Fact]
    public void DecodeState_reads_battery_noise_and_game_mode()
    {
        var reply = Assert.Single(Replies(Hex(StateFrame)));
        var state = Protocol.DecodeState(reply);

        Assert.NotNull(state);
        Assert.Equal(90, state.BatteryPercent);
        Assert.Equal(new NoiseSetting(NoiseMode.Off, "deep"), state.Noise);
        Assert.False(state.GameMode);
    }

    [Fact]
    public void DecodeState_ignores_other_replies()
    {
        var ack = Assert.Single(Replies(Hex("dd ee ff 09 01 01 a0 5c c6")));
        Assert.Null(Protocol.DecodeState(ack));
    }

    [Fact]
    public void Reply_matching_follows_the_command()
    {
        var ack = Assert.Single(Replies(Hex("dd ee ff 09 01 01 a0 5c c6")));
        var request = Protocol.SetNoise(new NoiseSetting(NoiseMode.Off), null)!;

        Assert.True(request.IsReply(ack));
        Assert.False(Protocol.ReadState().IsReply(ack));
    }

    [Fact]
    public void Parser_handles_frames_split_across_reads()
    {
        var all = Hex("dd ee ff 01 01 06 00 05 07 00 00 00 dd 67");
        var parser = new HituneParser();
        var frames = new List<HituneReply>();

        foreach (var b in all)
            frames.AddRange(parser.Feed([b]).OfType<HituneReply>());

        var parsed = Assert.Single(frames);
        Assert.Equal(new byte[] { 0x00, 0x05, 0x07, 0x00, 0x00, 0x00 }, parsed.Payload);
    }

    [Fact]
    public void Parser_handles_two_frames_and_leading_noise_in_one_read()
    {
        // O segundo quadro é sintético (CRC inventado): o parser não verifica o CRC recebido.
        var frames = Replies(Hex("00 11 dd ee ff 09 01 01 a0 5c c6 dd ee ff 09 01 01 a2 5d 86"));

        Assert.Equal(2, frames.Count);
        Assert.Equal(0xA0, frames[0].Payload[0]);
        Assert.Equal(0xA2, frames[1].Payload[0]);
    }

    [Fact]
    public void Parser_resyncs_when_a_stray_header_precedes_a_real_frame()
    {
        // Cabeçalho solto: lido como quadro, teria comprimento 255 e travaria a espera.
        var data = Hex("dd ee ff").Concat(Hex("dd ee ff 09 01 01 a0 5c c6")).ToArray();

        var parsed = Assert.Single(Replies(data));
        Assert.Equal(0xA0, parsed.Payload[0]);
    }

    [Fact]
    public void Parser_separates_an_unsolicited_event_from_the_ack_that_follows()
    {
        // Captura real ao ligar o modo jogo: aviso do fone e, em seguida, a confirmação.
        var messages = new HituneParser().Feed(Hex("85 86 87 02 04 01 dd ee ff 08 01 01 01 23 15"));

        Assert.Equal(2, messages.Count);
        Assert.Equal(new HituneEvent(0x02, 0x04, 0x01), messages[0]);
        Assert.True(Protocol.IsStateChangeHint(messages[0]));

        var ack = Assert.IsType<HituneReply>(messages[1]);
        Assert.Equal(HituneCommand.GameMode, ack.Command);
        Assert.False(Protocol.IsStateChangeHint(ack));
    }

    [Fact]
    public void Parser_reads_an_event_split_across_reads()
    {
        var parser = new HituneParser();
        Assert.Empty(parser.Feed(Hex("85 86")));
        Assert.Empty(parser.Feed(Hex("87 02 04")));

        var ev = Assert.IsType<HituneEvent>(Assert.Single(parser.Feed(Hex("00"))));
        Assert.Equal(new HituneEvent(0x02, 0x04, 0x00), ev);
    }

    [Theory]
    [InlineData(0xA0, NoiseMode.Off, "deep")]
    [InlineData(0xA1, NoiseMode.Anc, "deep")]
    [InlineData(0xB1, NoiseMode.Anc, "medium")]
    [InlineData(0xC1, NoiseMode.Anc, "light")]
    [InlineData(0xD1, NoiseMode.Anc, "adaptive")]
    [InlineData(0xA2, NoiseMode.Transparency, "deep")]
    [InlineData(0x01, NoiseMode.Anc, null)]
    public void Noise_byte_decodes_mode_and_level(byte raw, NoiseMode mode, string? level)
    {
        var payload = new byte[22];
        payload[3] = raw;
        var reply = new HituneReply(HituneCommand.DeviceState, 1, payload);

        Assert.Equal(new NoiseSetting(mode, level), Protocol.DecodeState(reply)!.Noise);
    }

    [Fact]
    public void Battery_above_100_is_reported_as_unknown()
    {
        var payload = new byte[22];
        payload[0] = 0xFF;
        var reply = new HituneReply(HituneCommand.DeviceState, 1, payload);

        Assert.Null(Protocol.DecodeState(reply)!.BatteryPercent);
    }
}
