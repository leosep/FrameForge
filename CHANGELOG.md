# Changelog

All notable changes to FrameForge are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.2] - 2026-10-14

### Security
- Backup files under `%ProgramData%\FrameForge` are now readable/writable by SYSTEM and Administrators only.
- Saved command values are validated (letters, digits, dashes) before being substituted into revert commands.

### Added
- Post-apply verification: a tweak is marked applied only if a live system check confirms the change stuck.
- Restore-point flow: the result is checked, the user is asked before continuing without one, and the 24h-creation-frequency limit is restored afterwards.
- Games tab: IFEO High-CPU priority is now optional via an (unchecked by default) checkbox with an anticheat warning; per-game markers use the full `.exe` path; empty IFEO keys are pruned on revert.
- Revert skips tweaks FrameForge never changed (“— no changes”).
- Self-test extended (DWord/String/QWord/Binary values, exit-code propagation, placeholder fallback) plus `FrameForge.exe --selftest` for headless runs.
- File logging to `%ProgramData%\FrameForge\logs` (14-day rotation).
- Cleaning skips reparse points/system entries and isolates per-folder errors.
- Full English UI (previously Spanish); installer speaks English/Spanish via `CustomMessages`.
- `EnableCompressionInSingleFile` (smaller download), version metadata 1.0.2, `.pdb` excluded from the installer.

### Fixed
- Game-tab handlers no longer touch UI controls from background threads.
- `Engine.Run` deadlock (sync stdout + fire-and-forget stderr) replaced with async reads on both streams; command output and exit codes are now logged and enforced.
- HKCU tweaks applied under a different elevated account are redirected to the interactive user's hive (`HKU\<SID>`), with a visible warning.
- Exact-original revert for power-plan values, service start modes, Nagle per-adapter values, `disablelastaccess`, hibernation, and the active power scheme (the duplicate Ultimate plan it created is deleted on revert).
- Mouse settings apply instantly via `SystemParametersInfo` (no sign-out needed for those).
- IFEO priority is mirrored to the 32-bit registry view so 32-bit games see it.

## [1.0.1 and earlier]

Early development history. See GitHub releases for published binaries: https://github.com/leosep/FrameForge/releases
