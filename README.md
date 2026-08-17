# Blare

A per-app volume mixer for Windows that watches what your listening is doing to
your hearing.

Windows only lets you turn app volumes down. Blare adds real above-100% boost,
per-app frequency analysis, and — the part nothing else does — tracking of how
long you've actually been listening loud, with protections that are deliberately
awkward to switch off.

Part of the **Blight** umbrella.

## Running it locally

`dotnet run` does **not** work. WinUI 3's XAML compiler and the MRT resource
tasks ship with Visual Studio rather than the .NET SDK, so a plain SDK build
fails inside `Microsoft.Build.Packaging.Pri.Tasks`. Use the script, which finds
the real MSBuild through `vswhere`:

```powershell
./scripts/run.ps1
```

```powershell
./scripts/run.ps1 -Configuration Release   # release build
./scripts/run.ps1 -Tray                    # start minimised to the tray
./scripts/run.ps1 -BuildOnly               # build without launching
```

It stops any running instance first, since a live process locks its own DLLs and
the build then fails on file copy.

Tests need none of that and run through the SDK normally:

```bash
dotnet test Blare.slnx
```

### Requirements

- Windows 11 (Windows 10 works; Mica falls back to Acrylic)
- Visual Studio with **.NET desktop development** and **WinUI application development**
- .NET 8 SDK

## Layout

| Project | What it holds |
|---|---|
| `src/Blare.Core` | Domain logic with no Windows dependency — safety tracking, consent, layout, settings. Where most tests live. |
| `src/Blare.Audio` | All COM/WASAPI interop: sessions, devices, per-process capture, FFT, boost pipeline. |
| `src/Blare.App` | The WinUI 3 app. |
| `tools/spikes` | Throwaway harnesses used to verify audio behaviour against real hardware before building on it. |

## Releases

Releases are **tag-driven**, and the tag is the only source of truth:

```bash
git tag v0.2.0
git push origin v0.2.0
```

That triggers the workflow, which runs the tests, stamps the version into the
assemblies and the MSIX manifest, and publishes a portable zip plus an MSIX to a
GitHub release. Nothing in the repository records a version, so there is no file
to forget to bump and no way for a file and a tag to disagree.

## Privacy

Blare is local. Per-app volumes, listening history, consent records and layout
all stay in `%LocalAppData%\Blight\Blare`.

The **only** network call in the product is the update check, which asks GitHub
for the latest release tag. It sends nothing about you, it can be turned off in
Settings, and it never installs anything without being asked.

## A note on measurement

Windows exposes relative signal level, never sound pressure at your ears — it
cannot know your speaker or headphone gain. Blare judges loudness from an app's
measured output scaled by the device volume, and says so everywhere it reports a
number. It is a useful relative signal, not a dosimeter.

## Licence

Not yet chosen.
