## What this changes

<!-- One thing per pull request. -->

## New or changed headset support

- Brand and model:
- Firmware version (if known):
- Windows version tested on:

### Verified on real hardware

<!-- List every control you declared and confirm it on your own headset. Say what you did NOT test. -->

| Control | Changed on the headset | Read back by the app |
|---------|:----------------------:|:--------------------:|
| Battery | | |
| Noise control | | |
| Game mode | | |

## Checklist

- [ ] `dotnet test tests/OpenHeadset.Tests` passes
- [ ] Tests use real captured bytes (synthetic ones are marked as synthetic)
- [ ] `docs/protocols/<brand>-<model>.md` describes what I observed
- [ ] Every declared capability was confirmed on real hardware
- [ ] No decompiled vendor code, keys, firmware or vendor assets anywhere in this change
- [ ] No firmware update, factory reset or other destructive command
