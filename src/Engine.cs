using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FrameForge;

public record Saved(bool Existed, int Kind, string? Value);
public record CmdResult(int ExitCode, string Output);

public static class Engine
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FrameForge");
    static readonly string File_ = Path.Combine(Dir, "backup.json");
    static readonly string CmdFile_ = Path.Combine(Dir, "cmdbackup.json");
    static readonly string LogDir = Path.Combine(Dir, "logs");
    static Dictionary<string, Saved> backup = new();
    // ORIGINAL-value backups for commands (powercfg, fsutil, hibernation,
    // active plan, services, Nagle) + “Applied::name” marker. Key => value.
    static Dictionary<string, string> cmdBackup = new();

    static readonly Regex GuidRe = new(@"([0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})", RegexOptions.Compiled);
    static readonly Regex HexRe = new(@"0[xX]([0-9a-fA-F]+)", RegexOptions.Compiled);
    // {orig:Key:default} in RevertCmds: replaced with the saved original value,
    // or with the typical “default” value if there is no backup (1st version, etc.).
    // Letters, digits and dashes only: nothing cmd.exe could interpret (&, |, >, ", etc.).
    static readonly Regex SafeTokenRe = new(@"^[A-Za-z0-9\-]{1,64}$", RegexOptions.Compiled);
    static readonly Regex OrigRe = new(@"\{orig:([^}:]+):([^}]*)\}", RegexOptions.Compiled);
    static string MarkKey(Tweak t) => "Applied::" + t.Name;

    static bool _secured;
    // Backups are read and restored as Administrator: if an unprivileged user
    // could edit them, they could make FrameForge write arbitrary values.
    // The folder is left to SYSTEM and Administrators only (SIDs, valid in any language).
    static void SecureDir()
    {
        if (_secured) return; _secured = true;
        try
        {
            Directory.CreateDirectory(Dir);
            Run($"icacls \"{Dir}\" /setowner *S-1-5-32-544 /T /C /Q");
            Run($"icacls \"{Dir}\" /inheritance:r /grant:r \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" /T /C /Q");
        }
        catch { }
    }

    public static void Load()
    {
        SecureDir();
        try { if (File.Exists(File_)) backup = JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(File_)) ?? new(); } catch { backup = new(); }
        try { if (File.Exists(CmdFile_)) cmdBackup = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CmdFile_)) ?? new(); } catch { cmdBackup = new(); }
    }
    static void Save() { Directory.CreateDirectory(Dir); File.WriteAllText(File_, JsonSerializer.Serialize(backup)); File.WriteAllText(CmdFile_, JsonSerializer.Serialize(cmdBackup)); }

    public static bool HasBackup(Tweak t)
    {
        if (cmdBackup.ContainsKey(MarkKey(t))) return true;
        return t.Reg.Any(r => backup.ContainsKey(r.Path + "|" + r.Name));
    }

    // ── File logging (%ProgramData%\FrameForge\logs). Must never break a tweak.
    static readonly object LogLock = new();
    public static void AppendFileLog(string s)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                try { foreach (var f in Directory.GetFiles(LogDir, "frameforge-*.log")) { try { if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14)) File.Delete(f); } catch { } } } catch { }
                File.AppendAllText(Path.Combine(LogDir, $"frameforge-{DateTime.Now:yyyyMMdd}.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}", new UTF8Encoding(true)); // with BOM: Notepad/PS read it correctly
            }
        }
        catch { }
    }

    // ── Real interactive user (elevated-HKCU problem) ──
    static string? _interactiveSid; static bool _sidResolved;
    static string? InteractiveSid()
    {
        if (_sidResolved) return _interactiveSid;
        _sidResolved = true;
        try
        {
            string? current = WindowsIdentity.GetCurrent().User?.Value;
            // explorer.exe always runs as the interactive (non-elevated) user:
            // its SID identifies the real profile HKCU tweaks belong to.
            // (Verified via WMI: Win32_Process.GetOwnerSid returns the “S-1-5-21-…” SID.)
            using var s = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name='explorer.exe'");
            foreach (ManagementObject o in s.Get())
            {
                using (o)
                {
                    string? sid = o.InvokeMethod("GetOwnerSid", null, null)?["Sid"]?.ToString();
                    if (!string.IsNullOrEmpty(sid) && sid != current) { _interactiveSid = sid; break; }
                }
            }
        }
        catch { _interactiveSid = null; }
        return _interactiveSid;
    }

    public static string HkcuTargetNote()
    {
        var sid = InteractiveSid();
        return sid == null ? ""
            : $"Note: the process is elevated under a different account. HKCU tweaks will be written to the interactive user's profile (SID {sid}), not the administrator's.";
    }

    static (RegistryKey root, string sub) Resolve(string p)
    {
        int i = p.IndexOf('\\');
        if (i < 0) throw new ArgumentException($"Invalid registry path: '{p}'.");
        var hive = p[..i]; var rest = p[(i + 1)..];
        if (hive == "HKLM") return (Registry.LocalMachine, rest);
        if (hive == "HKU") return (Registry.Users, rest);
        if (hive != "HKCU") throw new ArgumentException($"Hive desconocido en '{p}' (usa HKLM/HKCU/HKU).");
        var sid = InteractiveSid();
        // If we elevated with another account, the admin's HKCU is NOT the user's: redirect to HKU\<SID>.
        return sid == null ? (Registry.CurrentUser, rest) : (Registry.Users, sid + "\\" + rest);
    }

    // IFEO is read per view (32/64-bit) depending on the game: mirrored to Registry32
    // so 32-bit games see it too. Best-effort (the important part
    // is already in the 64-bit view).
    static void IfeoMirror(string path, string name, object? value, RegistryValueKind kind, Saved? revertTo)
    {
        if (!path.Contains("Image File Execution Options", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            string sub = path[(path.IndexOf('\\') + 1)..];
            using var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            if (revertTo == null && value != null) { using var k = b.CreateSubKey(sub); k?.SetValue(name, value, kind); }
            else
            {
                using var k = b.OpenSubKey(sub, true);
                if (k != null)
                {
                    if (revertTo == null || !revertTo.Existed) k.DeleteValue(name, false);
                    else k.SetValue(name, RestoreValue(revertTo, path + "|" + name), (RegistryValueKind)revertTo.Kind);
                }
            }
        }
        catch { }
    }

    static int ParseDword(string? v, string id)
    {
        // Tolerant: “-1” (int ToString) and “4294967295” (uint) are the same in a DWORD.
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (uint.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)) return unchecked((int)u);
        throw new InvalidOperationException($"Copia de seguridad ilegible ({id}): '{v}'.");
    }
    static long ParseQword(string? v, string id)
    {
        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        if (ulong.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)) return unchecked((long)u);
        throw new InvalidOperationException($"Copia de seguridad ilegible ({id}): '{v}'.");
    }
    static object RestoreValue(Saved s, string id) => (RegistryValueKind)s.Kind switch
    {
        RegistryValueKind.DWord => ParseDword(s.Value, id),
        RegistryValueKind.QWord => ParseQword(s.Value, id),
        RegistryValueKind.Binary => Convert.FromBase64String(s.Value ?? ""),
        // The catalog only uses DWord/QWord/String; other types restore as text.
        _ => (object)(s.Value ?? ""),
    };

    // ── Apply / Revert ──
    static string Short(string c) => c.Length > 90 ? c[..90] + "…" : c;

    public static void Apply(Tweak t, Action<string> log)
    {
        void L(string m) { AppendFileLog(m); log(m); }
        var errors = new List<string>();
        bool mouseTouched = false;
        foreach (var r in t.Reg)
        {
            try
            {
                var (root, sub) = Resolve(r.Path);
                using var k = root.CreateSubKey(sub, true);
                if (k == null) throw new InvalidOperationException($"Could not create/open the key '{r.Path}'.");
                string id = r.Path + "|" + r.Name;
                if (!backup.ContainsKey(id))
                {
                    var cur = k.GetValue(r.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    backup[id] = cur == null ? new(false, 0, null)
                        : new(true, (int)k.GetValueKind(r.Name), cur is byte[] bb ? Convert.ToBase64String(bb) : Convert.ToString(cur, CultureInfo.InvariantCulture));
                }
                k.SetValue(r.Name, r.Value, r.Kind);
                IfeoMirror(r.Path, r.Name, r.Value, r.Kind, null);
                if (r.Path.Contains(@"Control Panel\Mouse", StringComparison.OrdinalIgnoreCase)) mouseTouched = true;
            }
            catch (Exception ex) { errors.Add($"{r.Path} » {r.Name}: {ex.Message}"); }
        }
        Save();
        try { BackupCommandState(t, L); } catch (Exception ex) { L("   (warning: could not save the prior state: " + ex.Message + ")"); }

        var before = t.ApplyCmds.Any(c => c.Contains("duplicatescheme")) ? SchemeGuids() : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in t.ApplyCmds)
        {
            bool tolerant = raw.StartsWith("~"); // “~cmd”: benign failure (e.g. sc stop on an already stopped service)
            string c = tolerant ? raw[1..] : raw;
            // Duplicating the Ultimate plan creates a new GUID each time: if we are already on it, don't litter the list.
            if (c.Contains("duplicatescheme") && UltimateActive()) { L("   (already on the Ultimate plan: no extra duplicate created)"); continue; }
            L("   > " + Short(c, 220));
            try
            {
                var r = Run(c);
                if (!string.IsNullOrWhiteSpace(r.Output)) L("     " + r.Output.Replace("\n", "\n     "));
                if (r.ExitCode != 0 && !tolerant) errors.Add($"Command '{Short(c)}' failed (exit code {r.ExitCode}).");
                else L(r.ExitCode == 0 ? "     (exit code: 0)" : "     (tolerated warning, code " + r.ExitCode + ")");
            }
            catch (Exception ex)
            {
                if (tolerant) L("     (tolerated warning: " + ex.Message + ")");
                else errors.Add($"Command '{Short(c)}' could not run: {ex.Message}");
            }
            if (c.Contains("duplicatescheme")) CaptureCreatedScheme(before, L);
        }
        try { RefreshSystem(mouseTouched, L); } catch (Exception ex) { L("   (warning: system refresh: " + ex.Message + ")"); }
        // Only marked as applied if EVERYTHING went well (exit codes included).
        if (errors.Count > 0) throw new InvalidOperationException("Not fully applied:\n - " + string.Join("\n - ", errors));
        cmdBackup[MarkKey(t)] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        Save();
        // Real check: a command finishing without error doesn't guarantee the change stuck.
        if (!IsApplied(t))
            throw new InvalidOperationException("Commands finished without error, but the follow-up check shows the change did NOT stick (it may need a restart or may not be supported on this PC).");
    }

    public static void Revert(Tweak t, Action<string> log)
    {
        void L(string m) { AppendFileLog(m); log(m); }
        var errors = new List<string>();
        foreach (var r in t.Reg)
        {
            string id = r.Path + "|" + r.Name;
            try
            {
                if (!backup.TryGetValue(id, out var s)) { L($"   (no backup of {r.Name}: skipped)"); continue; }
                var (root, sub) = Resolve(r.Path);
                bool prune = false;
                using (var k = root.OpenSubKey(sub, true)) // Open (not Create): don't leave empty keys behind when reverting
                {
                    if (k == null) { backup.Remove(id); continue; }
                    if (!s.Existed) k.DeleteValue(r.Name, false);
                    else k.SetValue(r.Name, RestoreValue(s, id), (RegistryValueKind)s.Kind);
                    prune = !s.Existed && IsIfeo(r.Path) && k.ValueCount == 0 && k.SubKeyCount == 0;
                }
                IfeoMirror(r.Path, r.Name, null, r.Kind, s);
                if (prune) PruneIfeo(root, sub); // don't leave empty PerfOptions / exe keys behind
                backup.Remove(id);
            }
            catch (Exception ex) { errors.Add($"{r.Path} » {r.Name}: {ex.Message}"); }
        }
        Save();
        var used = new List<string>();
        foreach (var raw in t.RevertCmds)
        {
            bool tolerant = raw.StartsWith("~");
            string c = SubstituteOrig(tolerant ? raw[1..] : raw, used);
            L("   > " + Short(c, 220));
            try
            {
                var r = Run(c);
                if (!string.IsNullOrWhiteSpace(r.Output)) L("     " + r.Output.Replace("\n", "\n     "));
                if (r.ExitCode != 0 && !tolerant) errors.Add($"Command '{Short(c)}' failed (exit code {r.ExitCode}).");
                else L(r.ExitCode == 0 ? "     (exit code: 0)" : "     (tolerated warning, code " + r.ExitCode + ")");
            }
            catch (Exception ex)
            {
                if (tolerant) L("     (tolerated warning: " + ex.Message + ")");
                else errors.Add($"Command '{Short(c)}' could not run: {ex.Message}");
            }
        }
        RestoreCommandState(t, L, used);
        try { RefreshSystem(t.Reg.Any(r => r.Path.Contains(@"Control Panel\Mouse", StringComparison.OrdinalIgnoreCase)), L); } catch { }
        if (errors.Count > 0) throw new InvalidOperationException("Not fully reverted:\n - " + string.Join("\n - ", errors));
        cmdBackup.Remove(MarkKey(t));
        Save();
    }

    static bool IsIfeo(string p) => p.Contains("Image File Execution Options", StringComparison.OrdinalIgnoreCase);
    static void PruneIfeo(RegistryKey root, string sub)
    {
        try
        {
            root.DeleteSubKey(sub, false);
            int i = sub.LastIndexOf('\\');
            if (i < 0) return;
            string parent = sub[..i];
            if (parent.EndsWith("Image File Execution Options", StringComparison.OrdinalIgnoreCase)) return;
            bool empty;
            using (var pk = root.OpenSubKey(parent)) empty = pk != null && pk.ValueCount == 0 && pk.SubKeyCount == 0;
            if (empty) root.DeleteSubKey(parent, false);
        }
        catch { } // best-effort cleanup
    }

    static string Short(string c, int n) => c.Length > n ? c[..n] + "…" : c;

    // ── Prior-state backup for COMMANDS (not just registry) ──
    static void BackupCommandState(Tweak t, Action<string> L)
    {
        void Keep(string key, string? value)
        {
            // First time only: the ORIGINAL value must survive reapplications.
            if (value != null && !cmdBackup.ContainsKey(key)) cmdBackup[key] = value;
        }
        foreach (var raw in t.ApplyCmds)
        {
            string c = raw.StartsWith("~") ? raw[1..] : raw;
            try
            {
                if (c.Contains("PROCTHROTTLEMIN")) Keep("ProcThrottleMin", ReadPowerAcIndex("SUB_PROCESSOR", "PROCTHROTTLEMIN")?.ToString(CultureInfo.InvariantCulture));
                else if (c.Contains("SUB_PCIEXPRESS") && c.Contains("ASPM")) Keep("Aspm", ReadPowerAcIndex("SUB_PCIEXPRESS", "ASPM")?.ToString(CultureInfo.InvariantCulture));
                else if (c.StartsWith("ps:", StringComparison.OrdinalIgnoreCase) && c.Contains("duplicatescheme")) Keep("PowerScheme", ActiveScheme().Guid);
                else if (c.Contains("TcpAckFrequency")) BackupNagle();
                else if (c.Contains("DisableDeleteNotify", StringComparison.OrdinalIgnoreCase) && c.Contains("set", StringComparison.OrdinalIgnoreCase)) Keep("DisableDeleteNotify", QueryFsutilDigit("DisableDeleteNotify"));
                else if (c.Contains("disablelastaccess", StringComparison.OrdinalIgnoreCase) && c.Contains("set", StringComparison.OrdinalIgnoreCase)) Keep("DisableLastAccess", QueryFsutilDigit("disablelastaccess"));
                else if (c.Trim().Equals("powercfg -h off", StringComparison.OrdinalIgnoreCase)) Keep("Hibernate", HibernateOn() ? "on" : "off");
                else if (c.StartsWith("sc config", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(c, @"sc config\s+(\S+)", RegexOptions.IgnoreCase);
                    if (m.Success) Keep("Svc_" + m.Groups[1].Value, ScToken(ServiceStartMode(m.Groups[1].Value)));
                }
            }
            catch (Exception ex) { L($"   (warning: could not read the original value of '{Short(c)}': {ex.Message})"); }
        }
        Save();
    }

    static string SubstituteOrig(string cmd, List<string> used)
    {
        return OrigRe.Replace(cmd, m =>
        {
            string key = m.Groups[1].Value, def = m.Groups[2].Value;
            if (cmdBackup.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            {
                used.Add(key);
                if (SafeTokenRe.IsMatch(v)) return v;
                AppendFileLog($"WARNING: saved value '{key}' contains disallowed characters; using the typical value '{def}'.");
            }
            return def; // no backup: typical value documented in the catalog
        });
    }

    static void RestoreCommandState(Tweak t, Action<string> L, List<string> used)
    {
        try
        {
            if (t.RevertCmds.Any(c => c.Contains("TCPNoDelay") || c.Contains("TcpAckFrequency"))) RestoreNagle(L);
            if (t.RevertCmds.Any(c => c.Contains("-setactive", StringComparison.OrdinalIgnoreCase))
                && cmdBackup.TryGetValue("UltimateCreated", out var g) && Guid.TryParse(g, out _))
            {
                try
                {
                    string active = ActiveScheme().Guid;
                    if (!string.Equals(active, g, StringComparison.OrdinalIgnoreCase))
                    {
                        var r = Run($"powercfg /delete {Guid.Parse(g):D}");
                        if (r.ExitCode == 0) { L("   (Ultimate plan duplicated by FrameForge removed)"); cmdBackup.Remove("UltimateCreated"); }
                        else L($"   (warning: could not delete the duplicated plan: {r.Output})");
                    }
                    else L("   (the Ultimate plan is still active; its duplicate is kept)");
                }
                catch (Exception ex) { L($"   (warning: Ultimate plan cleanup: {ex.Message})"); }
            }
        }
        finally
        {
            foreach (var k in used) cmdBackup.Remove(k);
            Save();
        }
    }

    static void CaptureCreatedScheme(HashSet<string> before, Action<string> L)
    {
        // powercfg -duplicatescheme creates a new GUID each time: saved so it can be deleted on revert.
        try
        {
            if (cmdBackup.ContainsKey("UltimateCreated")) return;
            var after = SchemeGuids();
            after.ExceptWith(before);
            if (after.Count == 1) { cmdBackup["UltimateCreated"] = after.First(); Save(); }
            else L("   (warning: duplicated-plan GUID not identified; it won't be deleted on revert)");
        }
        catch (Exception ex) { L("   (warning: duplicated-plan GUID: " + ex.Message + ")"); }
    }

    // ── Real-state reads (“applied” detection) ──
    static int? ReadPowerAcIndex(string subAlias, string settingAlias)
    {
        // powercfg /query localizes labels per language, but the 0x… hex values don't:
        // “Current AC” is always second-to-last and “Current DC” last (verified:
        // ASPM's “Possible Setting Index: 000” lines carry no 0x prefix).
        try
        {
            var r = Run($"powercfg /query SCHEME_CURRENT {subAlias} {settingAlias}");
            if (r.ExitCode != 0) return null;
            var m = HexRe.Matches(r.Output);
            if (m.Count < 2) return null;
            return Convert.ToInt32(m[m.Count - 2].Groups[1].Value, 16);
        }
        catch { return null; }
    }

    static (string Guid, string Name) ActiveScheme()
    {
        var r = Run("powercfg /getactivescheme");
        if (r.ExitCode != 0) throw new InvalidOperationException("powercfg /getactivescheme failed: " + r.Output);
        foreach (var line in r.Output.Split('\n'))
        {
            var g = GuidRe.Match(line);
            if (g.Success && line.Contains('*'))
            {
                string name = "";
                int a = line.IndexOf('('), b = line.IndexOf(')');
                if (a >= 0 && b > a) name = line[(a + 1)..b];
                return (g.Groups[1].Value, name);
            }
        }
        throw new InvalidOperationException("Could not determine the active power plan.");
    }

    static HashSet<string> SchemeGuids()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (Match m in GuidRe.Matches(Run("powercfg /list").Output)) set.Add(m.Groups[1].Value); } catch { }
        return set;
    }

    static bool UltimateActive()
    {
        if (cmdBackup.TryGetValue("UltimateCreated", out var g) && !string.IsNullOrEmpty(g))
        { try { return string.Equals(ActiveScheme().Guid, g, StringComparison.OrdinalIgnoreCase); } catch { } }
        try
        {
            string name = ActiveScheme().Name;
            return name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)
                || name.Contains("ximo", StringComparison.OrdinalIgnoreCase); // “Máximo rendimiento” on Spanish Windows
        }
        catch { return false; }
    }

    static string? ServiceStartMode(string name)
    {
        // WMI returns StartMode in English (“Auto”/“Manual”/“Disabled”) regardless
        // of the Windows language; sc.exe, in contrast, localizes its labels.
        try
        {
            using var s = new ManagementObjectSearcher($"SELECT StartMode FROM Win32_Service WHERE Name='{name}'");
            foreach (ManagementObject o in s.Get()) { using (o) return o["StartMode"]?.ToString(); }
        }
        catch { }
        return null;
    }
    static string? ScToken(string? startMode) => startMode switch { "Auto" => "auto", "Manual" => "demand", "Disabled" => "disabled", _ => null };

    static string? QueryFsutilDigit(string what)
    {
        // “DisableLastAccess = 2 (…)”: the number after ‘=’ is never localized. For
        // DisableDeleteNotify the 1st line is NTFS (then ReFS).
        try
        {
            var r = Run($"fsutil behavior query {what}");
            if (r.ExitCode != 0) return null;
            var m = Regex.Match(r.Output, @"=\s*(\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    static bool HibernateOn()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            var v = k?.GetValue("HibernateEnabled");
            if (v != null) return Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0;
        }
        catch { }
        // On recent Windows 10/11 the key may not exist: hiberfil.sys is the available indicator.
        try { return File.Exists(@"C:\hiberfil.sys"); } catch { return true; }
    }

    const string NagleBase = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    static void BackupNagle()
    {
        // The Nagle script overwrites per-adapter values: previous ones are saved
        // to restore them exactly (Undo alone would only delete them).
        using var b = Registry.LocalMachine.OpenSubKey(NagleBase);
        if (b == null) return;
        foreach (var iface in b.GetSubKeyNames())
        {
            using var k = b.OpenSubKey(iface);
            if (k == null) continue;
            foreach (var vn in new[] { "TcpAckFrequency", "TCPNoDelay" })
            {
                var cur = k.GetValue(vn);
                if (cur != null && !cmdBackup.ContainsKey($"Nagle_{iface}_{vn}"))
                    cmdBackup[$"Nagle_{iface}_{vn}"] = Convert.ToString(cur, CultureInfo.InvariantCulture) ?? "";
            }
        }
        Save();
    }
    static void RestoreNagle(Action<string> L)
    {
        var keys = cmdBackup.Keys.Where(k => k.StartsWith("Nagle_", StringComparison.Ordinal)).ToList();
        if (keys.Count == 0) return; // there were no previous values: Undo already deleted them, correct
        foreach (var key in keys)
        {
            try
            {
                // Key = Nagle_<interface-GUID>_<value>; the GUID has no ‘_’, so the last ‘_’ splits.
                string rest = key[6..];
                int last = rest.LastIndexOf('_');
                string iface = rest[..last], vn = rest[(last + 1)..];
                using var k = Registry.LocalMachine.CreateSubKey(NagleBase + "\\" + iface);
                k?.SetValue(vn, ParseDword(cmdBackup[key], key), RegistryValueKind.DWord);
                cmdBackup.Remove(key);
            }
            catch (Exception ex) { L($"   (warning: could not restore {key}: {ex.Message})"); }
        }
        Save();
    }
    static bool NagleApplied()
    {
        try
        {
            using var b = Registry.LocalMachine.OpenSubKey(NagleBase);
            if (b == null) return false;
            var ifaces = b.GetSubKeyNames();
            if (ifaces.Length == 0) return false;
            foreach (var iface in ifaces)
            {
                using var k = b.OpenSubKey(iface);
                if (k == null) continue;
                if (Convert.ToString(k.GetValue("TcpAckFrequency"), CultureInfo.InvariantCulture) != "1") return false;
                if (Convert.ToString(k.GetValue("TCPNoDelay"), CultureInfo.InvariantCulture) != "1") return false;
            }
            return true;
        }
        catch { return false; }
    }

    static bool BcdHas(string entry)
    {
        try { var r = Run("bcdedit"); return r.ExitCode == 0 && r.Output.Contains(entry, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static bool NumEquals(object? cur, object? exp)
    {
        try
        {
            static decimal D(object? o) => o switch
            {
                int i => i, uint u => u, long l => l, ulong u => u,
                short s => s, ushort u => u, byte b => b, sbyte s => s,
                string str when long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
                string str when uint.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
                _ => Convert.ToDecimal(o, CultureInfo.InvariantCulture),
            };
            return D(cur) == D(exp);
        }
        catch { return false; }
    }

    static bool RegMatches(RegOp r)
    {
        try
        {
            var (root, sub) = Resolve(r.Path);
            using var k = root.OpenSubKey(sub, false);
            if (k == null) return false;
            var cur = k.GetValue(r.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (cur == null) return false;
            if (r.Kind == RegistryValueKind.Binary) return cur is byte[] cb && r.Value is byte[] eb && cb.SequenceEqual(eb);
            return r.Kind is RegistryValueKind.DWord or RegistryValueKind.QWord
                ? NumEquals(cur, r.Value)
                : string.Equals(Convert.ToString(cur, CultureInfo.InvariantCulture), Convert.ToString(r.Value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // null = not verifiable live (ignored unless there is no signal at all).
    static bool? CheckCmdApplied(string raw)
    {
        string c = raw.StartsWith("~") ? raw[1..] : raw;
        try
        {
            if (c.Contains("PROCTHROTTLEMIN")) return ReadPowerAcIndex("SUB_PROCESSOR", "PROCTHROTTLEMIN") == 100;
            if (c.Contains("SUB_PCIEXPRESS") && c.Contains("ASPM")) return ReadPowerAcIndex("SUB_PCIEXPRESS", "ASPM") == 0;
            if (c.Contains("duplicatescheme")) return UltimateActive();
            if (c.StartsWith("bcdedit /set disabledynamictick", StringComparison.OrdinalIgnoreCase)) return BcdHas("disabledynamictick");
            if (c.StartsWith("sc config", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(c, @"sc config\s+(\S+)\s+start=\s*(\S+)", RegexOptions.IgnoreCase);
                if (!m.Success) return null;
                return string.Equals(ScToken(ServiceStartMode(m.Groups[1].Value)), m.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
            }
            if (c.StartsWith("sc stop", StringComparison.OrdinalIgnoreCase) || c.StartsWith("sc start", StringComparison.OrdinalIgnoreCase)) return null;
            if (c.Contains("TcpAckFrequency")) return NagleApplied();
            if (c.Contains("DisableDeleteNotify", StringComparison.OrdinalIgnoreCase) && c.Contains("set", StringComparison.OrdinalIgnoreCase)) return QueryFsutilDigit("DisableDeleteNotify") == "0";
            if (c.Contains("disablelastaccess", StringComparison.OrdinalIgnoreCase) && c.Contains("set", StringComparison.OrdinalIgnoreCase)) return QueryFsutilDigit("disablelastaccess") == "1";
            if (c.Trim().Equals("powercfg -h off", StringComparison.OrdinalIgnoreCase)) return !HibernateOn();
            return null;
        }
        catch { return null; }
    }

    public static bool IsApplied(Tweak t)
    {
        try
        {
            foreach (var r in t.Reg) if (!RegMatches(r)) return false;
            bool anySignal = t.Reg.Count > 0;
            foreach (var c in t.ApplyCmds)
            {
                var v = CheckCmdApplied(c);
                if (v == false) return false;
                if (v == true) anySignal = true;
            }
            if (!anySignal) return cmdBackup.ContainsKey(MarkKey(t)); // e.g. hibernation: Engine marker
            return true;
        }
        catch { return false; }
    }

    // ── System refresh (instant mouse) ──
    const int SPI_SETMOUSE = 0x0004;
    const int SPIF_SENDCHANGE = 0x02;
    const int WM_SETTINGCHANGE = 0x001A;
    static readonly IntPtr HwndBroadcast = new(0xFFFF);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(int uiAction, int uiParam, int[]? pvParam, int fWinIni);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string? lParam, int fuFlags, int uTimeout, out IntPtr result);

    static void RefreshSystem(bool mouseTouched, Action<string> L)
    {
        // Mouse values apply instantly via SPI_SETMOUSE
        // (pvParam = {threshold1, threshold2, speed}); the rest is broadcast via
        // WM_SETTINGCHANGE. Visual effects/HAGS/bcdedit may still need
        // sign-out or restart (the catalog flags it with “restart”).
        if (mouseTouched)
        {
            try
            {
                var (root, sub) = Resolve(@"HKCU\Control Panel\Mouse");
                using var k = root.OpenSubKey(sub, false);
                long G(string n)
                {
                    try { return Convert.ToInt64(Convert.ToString(k?.GetValue(n), CultureInfo.InvariantCulture) ?? "0", CultureInfo.InvariantCulture); }
                    catch { return 0; }
                }
                int[] vals = { (int)G("MouseThreshold1"), (int)G("MouseThreshold2"), (int)G("MouseSpeed") };
                if (SystemParametersInfo(SPI_SETMOUSE, 0, vals, SPIF_SENDCHANGE))
                    L("   (mouse applied instantly, no sign-out needed)");
            }
            catch { } // best-effort
        }
        try { SendMessageTimeout(HwndBroadcast, WM_SETTINGCHANGE, IntPtr.Zero, null, 0x0002, 2000, out _); } catch { }
    }

    /// “ps:” = PowerShell (via -EncodedCommand Unicode: no quoting
    /// or special-character issues), otherwise cmd.exe. Returns code + output.
    public static CmdResult Run(string cmd, int timeoutMs = 120000)
    {
        ProcessStartInfo psi = cmd.StartsWith("ps:", StringComparison.Ordinal)
            ? new("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd[3..])))
            : new("cmd.exe", "/c " + cmd);
        psi.CreateNoWindow = true; psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        using var p = new Process { StartInfo = psi };
        var so = new StringBuilder(); var se = new StringBuilder();
        using var soDone = new ManualResetEventSlim(); using var seDone = new ManualResetEventSlim();
        p.OutputDataReceived += (_, e) => { if (e.Data == null) soDone.Set(); else so.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data == null) seDone.Set(); else se.AppendLine(e.Data); };
        p.Start();
        // Async reads on BOTH streams: avoids the classic stdout/stderr deadlock
        // of the original (it read stdout synchronously and stderr async without waiting).
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException($"Command took longer than {timeoutMs / 1000} s: {Short(cmd, 120)}");
        }
        soDone.Wait(5000); seDone.Wait(5000);
        string body = (so.ToString() + se.ToString()).Trim();
        return new(p.ExitCode, body.Length > 4000 ? body[..4000] + "…" : body);
    }

    // Creates the point and CHECKS the result. Temporarily lifts the 1-point-per-24h limit
    // and restores it as it was (it used to stay changed forever).
    const string PS_RESTORE = @"$k='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
$old=(Get-ItemProperty $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
Set-ItemProperty $k SystemRestorePointCreationFrequency 0 -Type DWord
try {
  Enable-ComputerRestore -Drive ""$env:SystemDrive\"" -ErrorAction Stop
  Checkpoint-Computer -Description 'FrameForge' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
} finally {
  if ($null -eq $old) { Remove-ItemProperty $k SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
  else { Set-ItemProperty $k SystemRestorePointCreationFrequency $old -Type DWord }
}";
    public static CmdResult RestorePoint() => Run("ps:" + PS_RESTORE, 300000);

    public static long Clean()
    {
        long freed = 0;
        // IgnoreInaccessible: EnumerateFiles used to throw on protected folders and abort the whole cleanup.
        // (System.IO qualified: System.Management brings another EnumerationOptions.)
        var opt = new System.IO.EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        foreach (var d in new[] { Path.GetTempPath(), @"C:\Windows\Temp" })
        {
            try
            {
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "*", opt))
                    try { var fi = new FileInfo(f); long l = fi.Length; fi.Delete(); freed += l; } catch { }
                foreach (var sd in Directory.EnumerateDirectories(d))
                    try { if ((File.GetAttributes(sd) & FileAttributes.ReparsePoint) == 0) Directory.Delete(sd, true); } catch { }
            }
            catch { } // one unreadable temp folder must not abort the rest
        }
        try { Run("ipconfig /flushdns"); } catch { }
        return freed;
    }

    public static Tweak GameTweak(string exe, bool cpuPriority = true)
    {
        var name = Path.GetFileName(exe);
        // High-performance GPU (key on dual-GPU laptops) + FSO disabled
        // + High priority. Engine.Apply handles the 32/64-bit IFEO mirror.
        var t = new Tweak { Category = "Specific game", Name = exe }; // Name = full path: the “applied” marker can't collide between two games with the same .exe
        t.Reg.Add(new(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", exe, "GpuPreference=2;", RegistryValueKind.String));
        t.Reg.Add(new(@"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", exe, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE", RegistryValueKind.String));
        if (cpuPriority) t.Reg.Add(new(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + name + @"\PerfOptions", "CpuPriorityClass", 3));
        return t;
    }

    // Engine self-test with a temporary key (HKCU\Software\FrameForgeTests,
    // redirected to the interactive user if elevated with another account). Used by the
    // “Self-test” button and `FrameForge.exe --selftest`. Cleans up everything when done.
    public static bool SelfTest(Action<string> log)
    {
        const string Base = @"HKCU\Software\FrameForgeTests";
        bool ok = true;
        void Check(bool cond, string msg) { log((cond ? "  [OK] " : "  [FAIL] ") + msg); if (!cond) ok = false; }
        var t = new Tweak { Category = "test", Name = "selftest", Description = "test", Risk = Risk.Safe, Recommended = false };
        t.D(Base, "PruebaDWord", 123).S(Base, "PruebaString", "hola ñ").Q(Base + @"\Sub", "PruebaQWord", 9876543210L)
            .B(Base, "PruebaBinary", new byte[] { 1, 2, 3, 255 }).B(Base, "PruebaPrevio", new byte[] { 1, 2 });
        void Wipe() { try { var (r, s) = Resolve(Base); r.DeleteSubKeyTree(s, false); } catch { } }
        log("Engine self-test (temporary HKCU\\Software\\FrameForgeTests key)…");
        try
        {
            Wipe();
            { var (pr, ps) = Resolve(Base); using var pk = pr.CreateSubKey(ps, true); pk.SetValue("PruebaPrevio", new byte[] { 9, 8, 7 }, RegistryValueKind.Binary); }
            Apply(t, m => log("  " + m));
            Check(IsApplied(t), "written values are detected as “applied”");
            Check(ParseDword("-1", "test") == -1, "DWORD -1 (NetworkThrottlingIndex) restores as -1");
            var r = Run("echo selftest-ok");
            Check(r.ExitCode == 0 && r.Output.Contains("selftest-ok"), "Engine.Run executes commands and captures output");
            Check(Run("cmd /c exit 42").ExitCode == 42, "Engine.Run propagates the exit code (42)");
            Check(SubstituteOrig("x {orig:Missing:def}", new List<string>()) == "x def", "missing backup falls back to the documented typical value");
            // Informational powercfg reads (never fail the test: they depend on the Windows edition/language).
            try { log("  [INFO] active scheme: " + ActiveScheme().Guid); var pm = ReadPowerAcIndex("SUB_PROCESSOR", "PROCTHROTTLEMIN"); log("  [INFO] PROCTHROTTLEMIN (AC) read: " + (pm?.ToString() ?? "not available")); }
            catch (Exception ex) { log("  [INFO] powercfg: " + ex.Message); }
            Revert(t, m => log("  " + m));
            var (root, sub) = Resolve(Base);
            using var k = root.OpenSubKey(sub, false);
            // DeleteValue removes VALUES (empty keys may remain, as Windows does): values are checked.
            using var ks = k?.OpenSubKey("Sub", false);
            Check(k == null || k.GetValue("PruebaDWord") == null, "revert removes the DWord test value");
            Check(k == null || k.GetValue("PruebaString") == null, "revert removes the String test value");
            Check(ks == null || ks.GetValue("PruebaQWord") == null, "revert removes the QWord test value");
            Check(k == null || k.GetValue("PruebaBinary") == null, "revert removes the binary test value");
            Check(k?.GetValue("PruebaPrevio") is byte[] pv && pv.SequenceEqual(new byte[] { 9, 8, 7 }), "revert restores a PRE-EXISTING binary value byte for byte");
            Check(!HasBackup(t), "the test backup is consumed on revert");
        }
        catch (Exception ex) { log("  [FAIL] exception: " + ex.Message); ok = false; }
        finally { try { Revert(t, _ => { }); } catch { } Wipe(); }
        log(ok ? "Self-test passed." : "Self-test FAILED (see lines above).");
        return ok;
    }
}
