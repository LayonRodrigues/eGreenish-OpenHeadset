# Contributing

Thanks for helping. The project exists so that a headset without desktop software can be controlled from Windows,
and so that anyone can add support for another headset, from any brand. The most useful contribution is a new
**profile**: see [docs/adding-a-headset.md](docs/adding-a-headset.md).

## Rules that protect your headset and the project

These are not negotiable, because they are what keeps people's hardware safe and the repository legal to host.

1. **Your own hardware only.** Test on a headset you own.
2. **Observed, not guessed.** Only add a command after you saw the vendor's own app send it to your headset and saw
   the headset accept it. No brute-forcing, no trying values to see what happens, no fuzzing a real headset.
3. **Confirm every write.** After sending a change, read the state back and check that it changed. A control that is
   not confirmed this way does not ship.
4. **Nothing destructive.** No firmware update or flashing, no factory reset, no pairing or account changes, no
   commands that rewrite persistent data you cannot restore.
5. **Clean room.** Describe the wire format you **observed**. Do not submit decompiled or disassembled vendor code,
   extracted keys or secrets, firmware images, or the vendor's assets (icons, strings, artwork) in any form:
   not in code, comments, docs, tests, issues or pull requests. If a headset needs a secret key to talk to it, say
   so in the issue and do not include the key.
6. **No telemetry, ads, accounts or network calls.** The app only talks to a headset on the local machine.
7. **Unknown stays unknown.** A field you could not verify is `null` or not implemented; it is never filled with a
   plausible value.

A pull request that breaks rules 4 or 5 will be closed, and anything that should not have been published will be
removed from the history.

## Development

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build OpenHeadset.slnx
dotnet test tests/OpenHeadset.Tests
dotnet run --project src/OpenHeadset.App
```

- `OpenHeadset.Core` and `OpenHeadset.Profiles` must not reference Windows, UI or serial APIs, so they stay testable
  with plain bytes.
- Keep a brand's code in its own folder under `src/OpenHeadset.Profiles/`.
- Tests that describe a headset use **real captured bytes**. Mark a synthetic byte string as synthetic.
- Keep pull requests small and about one thing.

## Reporting a problem

Use the issue templates. For a bug, include the headset model, firmware version if you know it, Windows version, and
what you expected. For a new headset, use the *Headset support request* template; you can help even without writing
code, for example by sharing a capture made with `tools/OpenHeadset.Probe`.

## Licence

By contributing you agree that your contribution is released under the project's [MIT licence](LICENSE).
