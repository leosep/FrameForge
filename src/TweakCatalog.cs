namespace FrameForge;

public static class TweakCatalog
{
    const string GC = @"HKCU\System\GameConfigStore";
    const string MM = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    const string EXP = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    const string PS_NAGLE = @"Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces' | ForEach-Object { Set-ItemProperty $_.PSPath TcpAckFrequency 1 -Type DWord; Set-ItemProperty $_.PSPath TCPNoDelay 1 -Type DWord }";
    const string PS_NAGLE_UNDO = @"Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces' | ForEach-Object { Remove-ItemProperty $_.PSPath TcpAckFrequency -ErrorAction SilentlyContinue; Remove-ItemProperty $_.PSPath TCPNoDelay -ErrorAction SilentlyContinue }";
    const string PS_ULTIMATE = @"$o = powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61; if ($o -match '([0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12})') { powercfg -setactive $Matches[1] }";

    static Tweak T(string cat, string name, string desc, Risk risk, bool rec, bool reboot = false)
        => new() { Category = cat, Name = name, Description = desc, Risk = risk, Recommended = rec, Reboot = reboot };
    // NOTE: Engine.Revert replaces {orig:Key:default} with the ORIGINAL value
    // saved before applying (cmdbackup.json), or the typical value if there is no backup.
    // "~cmd" = tolerated failure (e.g. "sc stop" on an already stopped service returns 1062).

    public static List<Tweak> All() => new()
    {
        // ───── GAMING ─────
        T("1 · Gaming", "Enable Windows Game Mode", "Prioritizes the foreground game and stops Windows Update from installing drivers or restarting while you play.", Risk.Safe, true)
            .D(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1).D(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1),
        T("1 · Gaming", "Disable Game DVR / background recording", "Removes Xbox Game Bar background recording, which uses GPU/CPU and can lower FPS.", Risk.Safe, true)
            .D(GC, "GameDVR_Enabled", 0).D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0).D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0),
        T("1 · Gaming", "Hardware-accelerated GPU scheduling (HAGS)", "Lowers latency and CPU overhead on modern GPUs (NVIDIA GTX 10+/RTX, Radeon RX 5000+). May not help older GPUs.", Risk.Medium, true, true)
            .D(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2),
        T("1 · Gaming", "Optimizations for windowed games (flip model)", "DX10/11 games in windowed or borderless mode use the modern presentation path: less latency and VRR support.", Risk.Safe, true)
            .S(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;"),
        T("1 · Gaming", "Disable fullscreen optimizations (global)", "Forces real exclusive fullscreen, with less input lag in many games.", Risk.Medium, true)
            .D(GC, "GameDVR_FSEBehaviorMode", 2).D(GC, "GameDVR_HonorUserFSEBehaviorMode", 1).D(GC, "GameDVR_DXGIHonorFSEWindowsCompatible", 1).D(GC, "GameDVR_EFSEFeatureFlags", 0),
        T("1 · Gaming", "Disable mouse acceleration", "1:1 mouse movement, steadier aim (consistent pointing).", Risk.Safe, true)
            .S(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0").S(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0").S(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0"),
        T("1 · Gaming", "Disable Sticky Keys prompts", "Stops pressing Shift 5 times from kicking you out of the game (Sticky/Filter/Toggle Keys).", Risk.Safe, true)
            .S(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "506").S(@"HKCU\Control Panel\Accessibility\ToggleKeys", "Flags", "58").S(@"HKCU\Control Panel\Accessibility\Keyboard Response", "Flags", "122"),

        // ───── CPU / POWER ─────
        T("2 · CPU & Power", "“Ultimate Performance” power plan", "Enables the hidden Ultimate Performance plan: no power saving that can cause stutters.", Risk.Safe, true)
            .Do("ps:" + PS_ULTIMATE).Undo("powercfg -setactive {orig:PowerScheme:381b4222-f694-41f0-9685-ff5bb260df2e}"),
        T("2 · CPU & Power", "Minimum processor state at 100%", "The CPU never clocks down: removes clock ramp-up latency. More heat and power draw (on laptops, stay plugged in).", Risk.Medium, false)
            .Do("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100", "powercfg /setactive SCHEME_CURRENT")
            .Undo("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {orig:ProcThrottleMin:5}", "powercfg /setactive SCHEME_CURRENT"),
        T("2 · CPU & Power", "Disable PCI Express power saving", "Keeps the GPU/NVMe out of low-power states (ASPM) that cause latency spikes.", Risk.Safe, true)
            .Do("powercfg /setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0", "powercfg /setactive SCHEME_CURRENT")
            .Undo("powercfg /setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM {orig:Aspm:1}", "powercfg /setactive SCHEME_CURRENT"),
        T("2 · CPU & Power", "CPU priority for foreground programs", "Win32PrioritySeparation = 0x26: preferential CPU time for the active game.", Risk.Safe, true)
            .D(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38),
        T("2 · CPU & Power", "Game-oriented multimedia profile (MMCSS)", "SystemResponsiveness 0 and a “Games” task with high GPU/CPU/disk priority.", Risk.Safe, true)
            .D(MM, "SystemResponsiveness", 0).D(MM + @"\Tasks\Games", "GPU Priority", 8).D(MM + @"\Tasks\Games", "Priority", 6)
            .S(MM + @"\Tasks\Games", "Scheduling Category", "High").S(MM + @"\Tasks\Games", "SFIO Priority", "High"),
        T("2 · CPU & Power", "High-precision timer (disabledynamictick)", "Reduces timer jitter on some PCs. Results vary; increases power draw.", Risk.Advanced, false, true)
            .Do("bcdedit /set disabledynamictick yes").Undo("bcdedit /deletevalue disabledynamictick"),

        // ───── VISUAL ─────
        T("3 · Visual Effects", "Visual effects set to “best performance”", "Frees desktop CPU/GPU/RAM resources while you play.", Risk.Safe, true)
            .D(EXP, "VisualFXSetting", 2),
        T("3 · Visual Effects", "Disable transparency effects", "Reduces desktop GPU usage (useful on Intel integrated graphics / AMD APUs).", Risk.Safe, true)
            .D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0),
        T("3 · Visual Effects", "Disable animations and menu delay", "A snappier interface.", Risk.Safe, true)
            .S(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0").S(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "0"),

        // ───── BACKGROUND ─────
        T("4 · Background & Services", "Disable SysMain (Superfetch)", "Less background disk/RAM usage. Recommended with an SSD; may be worse on HDDs.", Risk.Medium, true)
            .Do("sc config SysMain start= disabled", "~sc stop SysMain").Undo("sc config SysMain start= {orig:Svc_SysMain:auto}", "~sc start SysMain"),
        T("4 · Background & Services", "Reduce telemetry (DiagTrack)", "Stops the telemetry service and limits data collection.", Risk.Safe, true)
            .D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)
            .Do("sc config DiagTrack start= disabled", "~sc stop DiagTrack").Undo("sc config DiagTrack start= {orig:Svc_DiagTrack:auto}", "~sc start DiagTrack"),
        T("4 · Background & Services", "Block background apps", "Stops Store apps from using CPU/network while you play.", Risk.Safe, true)
            .D(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
        T("4 · Background & Services", "Disable Delivery Optimization (Windows Update P2P)", "Stops Windows from using your bandwidth to upload updates to other PCs.", Risk.Safe, true)
            .D(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DODownloadMode", 0),

        // ───── NETWORK ─────
        T("5 · Network (Ping & Latency)", "Disable Nagle's algorithm", "TcpAckFrequency=1 and TCPNoDelay=1 on all adapters: lower latency in TCP games.", Risk.Medium, true)
            .Do("ps:" + PS_NAGLE).Undo("ps:" + PS_NAGLE_UNDO),
        T("5 · Network (Ping & Latency)", "Remove multimedia network throttling", "NetworkThrottlingIndex = disabled (the default value throttles packets/ms).", Risk.Safe, true)
            .D(MM, "NetworkThrottlingIndex", -1),

        // ───── DISK ─────
        T("6 · Disk", "Ensure TRIM on SSD", "Maintains SSD write performance over time.", Risk.Safe, true)
            .Do("fsutil behavior set DisableDeleteNotify 0"),
        T("6 · Disk", "Disable NTFS “last access” updates", "Fewer metadata writes when opening thousands of game files.", Risk.Safe, true)
            .Do("fsutil behavior set disablelastaccess 1").Undo("fsutil behavior set disablelastaccess {orig:DisableLastAccess:2}"),
        T("6 · Disk", "Disable hibernation and fast startup", "Frees several GB (hiberfil.sys) and avoids driver issues after “shutting down”. Only if you don't use hibernate.", Risk.Medium, false)
            .Do("powercfg -h off").Undo("powercfg -h {orig:Hibernate:on}"),
    };
}
