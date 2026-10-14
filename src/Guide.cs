using System.Text;

namespace FrameForge;

public static class Guide
{
    public static string Build(SysInfo i)
    {
        var b = new StringBuilder();
        b.AppendLine("GUIDE FOR YOUR GPU: " + string.Join(" + ", i.Gpus)).AppendLine();
        if (i.Nvidia) b.AppendLine("═══ NVIDIA (NVIDIA Control Panel → Manage 3D Settings) ═══\n" +
            "• Power management mode: Prefer maximum performance\n" +
            "• Low Latency Mode: On/Ultra (with G-SYNC use “On” + V-Sync ON in the panel + an FPS cap 3 below the refresh rate)\n" +
            "• Texture filtering – quality: High performance\n" +
            "• Threaded optimization: Auto · Shader cache size: Unlimited or 10 GB\n" +
            "• Install a clean “Game Ready” driver (DDU in safe mode if coming from AMD/another driver).\n" +
            "• Enable ReBAR/Resizable BAR in BIOS if your GPU (RTX 30+) and CPU support it.\n" +
            "• DLSS / Frame Generation in the game itself: the biggest possible FPS gain on RTX.\n");
        if (i.Amd) b.AppendLine("═══ AMD Radeon (Adrenalin → Gaming → Global) ═══\n" +
            "• Radeon Anti-Lag: ON (avoid Anti-Lag+ in games with anticheat that blocks it)\n" +
            "• Radeon Chill: OFF · Radeon Boost: OFF/ON only if you accept resolution drops while moving\n" +
            "• Tessellation mode: Override application settings · Texture filtering: Performance\n" +
            "• Wait for vertical refresh: Off, unless you use FreeSync (then ON/Enhanced Sync)\n" +
            "• FSR / AFMF (Fluid Motion Frames) to raise FPS in any DX11/12 game.\n" +
            "• Smart Access Memory (= Resizable BAR) in BIOS with a Ryzen CPU.\n" +
            "• On Radeon APUs/iGPUs: raise dedicated video memory (UMA Frame Buffer) in BIOS to 2–4 GB.\n");
        if (i.Intel) b.AppendLine("═══ Intel Graphics (UHD / Iris Xe / Arc) ═══\n" +
            "• BIGGEST impact: use DUAL-CHANNEL RAM (2 identical sticks). iGPUs share RAM: dual channel can give +30–80% FPS.\n" +
            "• Intel Graphics Command Center: System → Power → “Maximum performance” (plugged in).\n" +
            "• Update the driver with Intel Driver & Support Assistant.\n" +
            "• Play at 720p/900p with upscaling, Low quality, shadows and antialiasing off; use XeSS if the game supports it.\n" +
            "• In BIOS: raise DVMT Pre-Allocated if present. Windows shares up to ~50% of RAM as graphics memory.\n" +
            "• Close Chrome/Edge and overlays while playing: they consume the same RAM as the graphics.\n" +
            "• Laptop with Intel + NVIDIA/AMD: force the dedicated GPU with FrameForge's “Games” tab.\n");
        b.AppendLine("═══ EVERYONE ═══\n" +
            "• Apply the recommended tweaks in FrameForge and restart.\n" +
            "• Disable overlays you don't use (Discord, Steam, GeForce Experience, Xbox Game Bar).\n" +
            "• Cap FPS slightly below the monitor refresh rate for lower latency and less heat.\n" +
            "• Keep Windows and the BIOS updated; temperatures under control (thermal paste, dust).\n" +
            "• Install the game on an SSD, and keep ≥15% free space.\n" +
            "• Exclude your games folder in Windows Defender ONLY if you trust the source.\n" +
            "• Monitor: select its maximum refresh rate in Settings → Display → Advanced display.\n" +
            "• Before buying anything: if the bottleneck is the GPU, lower resolution/shadows/RT; if it's the CPU, lower draw distance and crowd/NPC counts.");
        return b.ToString();
    }
}
