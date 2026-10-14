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

- `flag` was `01` in every capture.
- The CRC of received frames has **not** been reproduced; the app ignores it and relies on the header and
  the length. A resynchronising parser handles split frames, several frames per read and stray bytes.

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
| `08` | game mode | `00` off / `01` on | reply echoes the value |
| `09` | noise control | 1 byte, see below | reply echoes the value |

## Device state payload (22 bytes)

| offset | meaning |
|-------:|---------|
| 0 | battery percent (`0x5A` = 90) |
| 1, 2 | `FF` on this over-ear model (unused) |
| 3 | noise control byte |
| 4 | EQ preset (read only here) |
| 5 | dual-device link |
| 6 | game mode (`00`/`01`) |
| 7 | spatial/acoustics code |
| 8 – 15 | touch/button assignments (not decoded) |
| 19 | prompt volume |
| 20 | spatial audio flag |
| 21 | wear detection flag |

Offsets not listed were not interpreted.

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
