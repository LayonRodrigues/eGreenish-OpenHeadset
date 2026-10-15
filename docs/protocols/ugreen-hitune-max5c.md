# Wire protocol (as observed)

This document describes what was **observed on one headset** by talking to it over its Bluetooth serial
port and reading the replies. It is not a vendor specification. Anything not listed here has not been tested.

Tested on: one *HiTune Max5c* unit, Windows 10 build 19045. Version reply payload: `00 05 07 00 00 00`
(meaning not decoded).

## Transport

The headset exposes a Bluetooth serial service (SPP, UUID `00001101-0000-1000-8000-00805F9B34FB`). Windows
creates an outgoing COM port for it when the headset is paired. The app finds that port through the Windows
device IDs (the headset's Bluetooth address appears in both the device and the port), so the COM number does
not matter. 115200 8N1, DTR/RTS on.

## Frames

Computer → headset:

```
AA BB CC | cmd | len | payload (len bytes) | crc_lo | crc_hi
```

- CRC-16/MODBUS (reflected polynomial `0xA001`, initial value `0xFFFF`) over `cmd | len | payload`,
  little-endian. The headset accepts frames built this way.

Headset → computer (reply to a command):

```
DD EE FF | cmd | flag | len | payload (len bytes) | crc_lo | crc_hi
```

- `flag` is `01` in a normal reply.
- The CRC of received frames has **not** been reproduced; the app ignores it and relies on the header and
  the length. A resynchronising parser handles split frames, several frames per read and stray bytes.

**Short replies mean "refused".** When the headset refuses a command it answers with a 7-byte frame the length
field does not describe (`DD EE FF | cmd | status | 2 bytes`). Seen on one unit, both times with Hi-Res decoding on:

| bytes | when |
|-------|------|
| `DD EE FF 08 00 A6 94` | game mode refused (the "length" byte is `A6`) |
| `DD EE FF 06 01 01 FF` | dual link refused |

After Hi-Res was switched off, the same two commands got the normal 9-byte echo and the state changed
(game mode `DD EE FF 08 01 01 01 23 15`, dual link `DD EE FF 06 01 01 01 79 B7`).

A parser that trusts the length waits forever for a short frame, the request times out and the session drops
the connection. The app therefore treats bytes still pending after a quiet read interval as a short frame
(`HituneParser.Flush`) and reports it as a refusal.

Headset → computer (unsolicited notification, 6 bytes):

```
85 86 87 | type | sub | value
```

Seen when the game mode changes: `85 86 87 02 04 01` (on) and `85 86 87 02 04 00` (off), just before the
reply. The app treats any notification as a hint to read the state again.

## Commands

| cmd | name | payload | notes |
|----:|------|---------|-------|
| `01` | version | `00` | reply payload: 6 bytes |
| `04` | device state | `00` | reply payload: 22 bytes, layout below |
| `05` | equalizer preset | 1 byte, preset id | reply echoes the value; state offset 4 follows. Ids 0 to 12 were all accepted. |
| `06` | dual link (two devices at once) | `00` off / `01` on | reply echoes the value; state offset 5 follows. **Refused while Hi-Res decoding is on.** |
| `08` | game mode | `00` off / `01` on | reply echoes the value; state offset 6 follows. **Refused while Hi-Res decoding is on.** |
| `09` | noise control | 1 byte, see below | reply echoes the value |
| `0A` | button shortcuts | 8 bytes, one function code per gesture | always sends all 8; reply echoes them; state offsets 8 to 15 follow |
| `0B` | Hi-Res decoding | `00` off / `01` on | reply echoes the value; state offset 7 follows |
| `12` | spatial audio | `00` off / `01` on | reply echoes the value; state offset 20 follows |

Hi-Res decoding was found to be the blocker by changing only that setting: with it on, game mode and dual link
were refused; with it off, both were accepted and read back. The manufacturer's app also lists Hi-Res as a
conflict for other features (adaptive ANC and wind-noise reduction); this app has not checked those.

## Device state payload (22 bytes)

| offset | meaning |
|-------:|---------|
| 0 | battery percent. Seen: `0x5A` (90), `0x32` (50), `0x28` (40), always a multiple of 10 |
| 1, 2 | `FF` on this over-ear model (unused) |
| 3 | noise control byte |
| 4 | equalizer preset id |
| 5 | dual link (`00`/`01`) |
| 6 | game mode (`00`/`01`) |
| 7 | Hi-Res decoding (`00`/`01`). The manufacturer's app also reads this byte as an "acoustics code"; only `00` and `01` were seen |
| 8 – 15 | one function code per gesture, see below |
| 19 | prompt volume |
| 20 | spatial audio flag |
| 21 | wear detection flag |

Offsets not listed were not interpreted.

## Button shortcuts

Offsets 8 to 15 hold one function code per gesture, in this order: tap, double tap, triple tap, long press, then the
same four for the right side. On the tested unit the last four were all `00`. Command `0A` writes all eight at
once, so changing one gesture means sending the other seven as they were last read.

Function codes, taken from the manufacturer's app. Only **7** was sent to the headset (triple tap set to play/pause,
read back, then restored); the others are not individually verified:

| code | function |
|-----:|----------|
| `00` | none |
| `01` | answer / hang up |
| `02` | voice assistant |
| `03` | previous track |
| `04` | next track |
| `05` | volume up |
| `06` | volume down |
| `07` | play / pause |
| `08` | game mode |
| `09` | noise control |
| `0E` | AI recording |
| `0F` | sound effect mode |

The tested unit shipped with other codes in use (long press was `0B`, which is not in the table above), so a
client should show an unknown code as such and leave it alone unless the user changes it.

## Equalizer presets

Preset ids from the manufacturer's app: `0` classic, `1` jazz, `2` electronic, `3` pop, `4` classical, `5` rock,
`6` bass, `7` treble, `8` vocal, `9` cinema, `10` game, `11` podcast, `12` custom. The headset accepted and echoed all
of them. `12` needs band data (separate commands in the manufacturer's app) that this app does not send.

## Noise control byte

`(level << 4) | mode`

| mode | meaning |
|-----:|---------|
| `0` | off |
| `1` | active noise cancelling |
| `2` | transparency / ambient |

| level nibble | meaning (used with mode `1`) |
|-------------:|------------------------------|
| `A` | deep |
| `B` | medium |
| `C` | light |
| `D` | adaptive |

Examples, all confirmed by reading the state back: `A0` off (deep level remembered), `A1` ANC deep,
`B1` medium, `C1` light, `D1` adaptive, `A2` transparency. When switching to off or transparency, keep the
current level nibble, as the byte above shows.

## Captured examples

| direction | bytes |
|-----------|-------|
| → read state | `AA BB CC 04 01 00 31 91` |
| ← state | `DD EE FF 04 01 16 5A FF FF A0 00 00 00 00 08 00 00 07 00 00 00 00 00 00 00 09 00 00 BC BD` |
| → noise control `A0` | `AA BB CC 09 01 A0 A0 2A` |
| ← ack | `DD EE FF 09 01 01 A0 5C C6` |
| → noise control `A2` | `AA BB CC 09 01 A2 21 EB` |

## Not implemented

EQ writes, spatial audio, touch/button assignments, wear detection, dual-device link and firmware update were
not tested, so the app does not send them. Some models or firmware revisions may require an encrypted session
for writes; this unit accepted plain frames. If a write is not acknowledged the app reports it and does not
try anything else.
