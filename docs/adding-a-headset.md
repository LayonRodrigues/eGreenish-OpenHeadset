# Adding a headset

eGreenish OpenHeadset is built so that supporting a new headset, from any brand, means adding one **profile**.
You do not need to touch the window, the Bluetooth code or the tray. This guide walks through it.

> [!IMPORTANT]
> Read [CONTRIBUTING.md](../CONTRIBUTING.md) first, especially the safety and clean-room rules. In short: work only
> with your own headset, send only commands you have seen it accept, and never submit code or keys taken from a
> vendor's app.

## How the pieces fit

```
src/
  OpenHeadset.Core        neutral types and the contract every headset implements (no Windows, no UI)
  OpenHeadset.Profiles    one folder per brand; each model is a profile + a protocol   <-- you work here
  OpenHeadset.Transport   finds the headset in Windows and talks over its serial port (knows no brand)
  OpenHeadset.App         the tray window; it builds its controls from what a profile declares
tests/OpenHeadset.Tests   golden-byte tests per profile + a contract test that checks every profile
tools/OpenHeadset.Probe   capture tool: list ports, listen, send bytes you chose
```

A profile (`HeadsetProfile`) says what the headset is and what it can do. A protocol (`IHeadsetProtocol`) knows the
bytes. The app asks the protocol to build a request, sends it, and hands every reply back to the protocol to decode.
Nothing else in the project knows a brand's frame format.

## Step 1: check that your headset can be supported today

The Transport layer talks to the headset's **Bluetooth serial port (SPP)**, the same port Windows lists as
"Standard Serial over Bluetooth link (COMn)". Pair the headset, then run:

```powershell
dotnet run --project tools/OpenHeadset.Probe -- list
```

- Your headset appears with a COM port: it can be supported with the current code.
- It does not appear: it probably uses BLE only. Supporting it needs a new transport (see
  [Headsets that are BLE only](#headsets-that-are-ble-only)); open an issue before starting.

## Step 2: learn the protocol from your own headset

You need the bytes the vendor's app exchanges with the headset. Ways to get them, from least to most effort:

1. **Listen.** `probe listen COM5 60`, then press the headset's buttons and change settings from your phone.
   Some headsets announce changes on their own.
2. **Capture the phone app.** On Android, enable *Developer options → Bluetooth HCI snoop log*, use the vendor app
   (change one setting at a time and write down what you changed and when), then open the log in Wireshark.
   Filter by `btrfcomm` (serial) and read the payload bytes.
3. **Change one thing at a time.** The setting you toggled is the byte that changes. Read the state back after every
   write to confirm it.

What to look for: the frame layout (start marker, command, length, payload, checksum), how the headset answers, how
the state is reported, and whether it sends unsolicited notices.

> Clean-room rule: describe what you **observed on the wire**. Do not copy or paste decompiled vendor code, keys,
> firmware or assets into the repository, an issue or a pull request.

## Step 3: write down what you found

Create `docs/protocols/<brand>-<model>.md`, following
[ugreen-hitune-max5c.md](protocols/ugreen-hitune-max5c.md): transport, frame layout, checksum, commands, state layout
and a table of captured bytes. Mark anything you did not verify as not verified.

## Step 4: implement the profile

Create `src/OpenHeadset.Profiles/<Brand>/` and implement three things.

**1. The protocol** (`IHeadsetProtocol`), with no Windows or UI code:

| Member | What it does |
|--------|--------------|
| `CreateParser()` | returns an `IMessageParser`: turns received bytes into message objects. Must handle split frames, several frames per read and stray bytes. |
| `ReadState()` | the request that asks the headset for its state, and how to recognise the reply |
| `DecodeState(message)` | turns that reply into a `HeadsetState` (battery, noise setting, game mode); `null` if it is not a state reply |
| `SetNoise(desired, current)` | the request that sets the noise mode/level, or `null` if unsupported |
| `SetGameMode(enabled)` | the request that toggles game mode, or `null` if unsupported |
| `IsStateChangeHint(message)` | `true` for an unsolicited notice that means "state changed, read it again" |

A field you do not know stays `null` in `HeadsetState`. Do not guess.

**2. The profile** (`HeadsetProfile`):

```csharp
public static class AcmeBuds1Profile
{
    public static HeadsetProfile Create() => new(
        Id: "acme-buds-1",                       // lowercase, unique, never changes
        DisplayName: "Acme Buds 1",
        NameFragments: ["Acme Buds 1"],           // matched against the Bluetooth name; be specific
        Capabilities: Capabilities.Battery | Capabilities.NoiseControl,
        NoiseModes: [NoiseMode.Off, NoiseMode.Anc],
        NoiseLevels: [],                          // or e.g. new NoiseLevel("high", "High")
        Serial: new SerialSettings(),             // default: standard SPP, 115200
        CreateProtocol: () => new AcmeBuds1Protocol());
}
```

Declare only capabilities you **verified on real hardware**. The window shows exactly the controls you declare.

**3. Register it** by adding one line to `src/OpenHeadset.Profiles/BuiltInProfiles.cs`.

## Step 5: test it

```powershell
dotnet test tests/OpenHeadset.Tests
```

- The **contract test** (`ProfileContractTests`) already runs against every registered profile: unique id, specific
  name fragments, declared capabilities that match what the protocol can build, a parser that survives random bytes.
  If your profile is inconsistent, it tells you where.
- Add your own tests with **real captured bytes**: build the frames you send and assert they match what the headset
  accepted; feed captured replies to the parser and decode them. See
  `tests/OpenHeadset.Tests/Profiles/HituneMax5cTests.cs`.

## Step 6: verify on hardware

Run the app (`dotnet run --project src/OpenHeadset.App`) with your headset connected. For **every** control you
declared, check that the headset really changes, and that the app reads the change back. Restore your original
settings afterwards. The pull request template asks you to confirm this; say which model and firmware you tested,
and which controls you did not.

## Headsets that are BLE only

Some headsets expose their controls over Bluetooth Low Energy (GATT characteristics) and have no serial port. The
`Transport` layer does not handle that yet. A BLE transport would live next to `HeadsetLink` and implement the same
send/receive idea; `IHeadsetProtocol` would not need to change. If you want to build it, open an issue so the
transport interface can be agreed before the code is written.

## Check list before opening a pull request

- [ ] Profile folder, protocol, one line in `BuiltInProfiles.cs`
- [ ] `docs/protocols/<brand>-<model>.md` written from your own observations
- [ ] Tests with real captured bytes; `dotnet test` passes
- [ ] Every declared capability verified on real hardware, with the model and firmware stated
- [ ] No decompiled code, vendor keys, firmware or copyrighted assets anywhere in the change
- [ ] No firmware update, factory reset or other destructive command
