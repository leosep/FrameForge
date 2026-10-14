using System.Management;
namespace FrameForge;

public record SysInfo(string Cpu, List<string> Gpus, string Ram, string Os)
{
    public bool Nvidia => Gpus.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
    public bool Amd => Gpus.Any(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
    public bool Intel => Gpus.Any(g => g.Contains("Intel", StringComparison.OrdinalIgnoreCase));
}

public static class SystemInfo
{
    static string Q(string cls, string prop)
    {
        try { using var s = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}"); foreach (var o in s.Get()) return o[prop]?.ToString()?.Trim() ?? "?"; } catch { }
        return "?";
    }
    public static SysInfo Read()
    {
        var gpus = new List<string>();
        try { using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"); foreach (var o in s.Get()) gpus.Add(o["Name"]?.ToString() ?? "?"); } catch { }
        ulong.TryParse(Q("Win32_ComputerSystem", "TotalPhysicalMemory"), out var ram);
        return new(Q("Win32_Processor", "Name"), gpus, $"{ram / 1073741824.0:0.#} GB", Q("Win32_OperatingSystem", "Caption"));
    }

    // true if the PC reports a battery (laptop): used to warn before applying
    // tweaks that spike power draw/heat (e.g. minimum CPU at 100%).
    public static bool HasBattery()
    {
        try { using var s = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery"); foreach (var o in s.Get()) { using (o) return true; } } catch { }
        return false;
    }
}
