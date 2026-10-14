# FrameForge — Gaming Optimizer for Windows 10/11

![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D4)
![x64](https://img.shields.io/badge/arch-x64-green)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![WPF](https://img.shields.io/badge/UI-WPF-00C27A)

Creado por leandrosepulveda.com

FrameForge applies 25 documented, reversible Windows tweaks for gaming: fewer stutters, less latency, more free resources for your game. No magic FPS promises — every change is real, backed up, and revertible.

> **Download the latest release:** https://github.com/leosep/FrameForge/releases
> Grab **`FrameForge-Setup.exe`** (installer) or **`FrameForge.exe`** (portable).

---

## ⚠️ Safety first — read before applying

- **Administrator rights required.** Windows will show a UAC prompt on launch — this is expected: most tweaks live under `HKLM` or need privileged commands.
- **Create a restore point first.** The app offers to create one before applying (and verifies it worked). If creation fails, you'll be asked whether to continue without that safety net.
- **Everything is reversible.** Original registry values go to `C:\ProgramData\FrameForge\backup.json`, command originals (power plans, services, Nagle, …) to `cmdbackup.json`. Use *Revert selected* / *Revert all*.
- **Anticheat warning.** On the *Games* tab, *High CPU priority* (IFEO) is **off by default** — do NOT enable it for games with Vanguard, EAC, BattlEye or FACEIT.
- **Laptops.** *Minimum processor state at 100%* raises heat and power draw — the app asks for confirmation on battery-powered PCs. Stay plugged in.
- **The binary is not code-signed.** SmartScreen/antivirus may warn on first run. That is normal for an unsigned personal project.
- **No tweak multiplies FPS by magic.** Realistic benefit: fewer hitches, lower latency, less background noise.

## Install

| File | What | When to use |
|---|---|---|
| `FrameForge-Setup.exe` | Installer (Program Files + Start Menu) | Most users |
| `FrameForge.exe` | Portable single-file, self-contained (~69 MB, no .NET needed) | USB / no install |

On first launch, accept the UAC prompt. The *Optimize* tab shows your hardware and marks every tweak as **● applied** or **○ not applied** by reading the live system.

## Usage

- **Optimize tab** — tick tweaks, *Apply selected*. *Select recommended* is a sane default. Items flagged *restart required* need a reboot.
- **Games tab** — point FrameForge at a game's `.exe`: high-performance GPU preference, no fullscreen optimizations, optional High CPU priority. All per-game and reversible.
- **GPU Guide tab** — vendor-specific panel settings (NVIDIA / AMD / Intel) plus shortcuts to Windows graphics settings, NVIDIA Panel, power options and Device Manager.
- **About tab** — *Export/Import profile* (share your selection as JSON) and *Run self-test*.
- **Self-test** (`FrameForge.exe --selftest` also works headless) exercises the engine against a temporary `HKCU\Software\FrameForgeTests` key and cleans up afterwards.
- **Logs** live in `%ProgramData%\FrameForge\logs` (auto-rotated, 14 days).

### Uninstalling

Add/Remove programs removes the app, shortcuts and logs. `backup.json`/`cmdbackup.json` are intentionally **kept** so a reinstall can still revert — delete `%ProgramData%\FrameForge` by hand if you're done for good.

## The 25 tweaks

| Group | Tweaks |
|---|---|
| 1 · Gaming | Game Mode, Game DVR off, HAGS, windowed-game flip model, fullscreen optimizations off, no mouse accel, no Sticky-Keys prompts |
| 2 · CPU & Power | Ultimate Performance plan, min CPU 100%, PCIe ASPM off, foreground priority, MMCSS gaming profile, high-precision timer |
| 3 · Visual Effects | Best-performance visuals, no transparency, no animations/menu delay |
| 4 · Background & Services | SysMain off, DiagTrack telemetry down, background apps blocked, Delivery Optimization P2P off |
| 5 · Network | Nagle off (per-adapter, originals backed up), multimedia throttling off |
| 6 · Disk | SSD TRIM on, NTFS last-access off, hibernation + fast startup off |

Reverting a tweak restores the **exact original value** captured before applying (not a hardcoded default) — including power-plan values, service start modes, the previous active power scheme (its duplicate Ultimate plan is deleted), and per-adapter Nagle values.

## Build from source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download) and optionally [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bat
build.bat
```

This produces `publish\FrameForge.exe` (portable) and, with Inno installed, `dist\FrameForge-Setup.exe`. You can also open `src\FrameForge.sln` in Visual Studio 2022.

Project layout:

- `src/Tweak.cs` / `src/TweakCatalog.cs` — the 25-tweak catalog. To add one, append an entry in `TweakCatalog.All()`.
- `src/Engine.cs` — apply/revert with original-value backup, exit-code validation, post-apply verification.
- `src/SystemInfo.cs` / `src/Guide.cs` — WMI hardware detection and vendor GPU guide.
- `src/MainWindow.xaml(.cs)` — UI.
- `installer/setup.iss` — installer script (English + Spanish).

## Manual testing checklist (real Windows, ideally a VM or with a restore point)

1. `FrameForge.exe --selftest` → “Self-test passed.”
2. Apply *Game Mode*, *Game DVR*, *mouse*; reopen — they read back as “● applied”.
3. *Ultimate Performance*: `powercfg /list` shows one duplicate; revert → previous plan active, duplicate gone.
4. SysMain: apply/revert, confirm the original start mode returns (`sc qc SysMain`).
5. *Visual effects*: sign out and check; revert and sign out again.
6. Games tab with a real `.exe`; check `%ProgramData%\FrameForge\logs`.

## FAQ

- **Do I need to reinstall after a Windows update?** No — but re-check statuses; major updates can reset some keys.
- **Something broke — how do I undo everything?** *Revert all*, or restore the point created beforehand.
- **Can I use it on a laptop?** Yes, plugged in. Heed the battery warning for the 100%-CPU tweak.
- **Why does Windows flag the download?** The releases aren't code-signed yet (see Safety). Source is here for review; build it yourself if you prefer.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

No license file ships with this project yet — all rights reserved by default. If you want to reuse or redistribute FrameForge, contact leandrosepulveda.com first.
