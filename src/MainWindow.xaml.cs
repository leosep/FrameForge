using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;

namespace FrameForge;

public partial class MainWindow : Window
{
    readonly ObservableCollection<Tweak> tweaks = new(TweakCatalog.All());
    int _busy; // counter for reentrant Busy() (several async buttons)
    sealed record ProfileItem(string Name, bool IsSelected);

    public MainWindow()
    {
        InitializeComponent();
        Engine.Load();
        var v = CollectionViewSource.GetDefaultView(tweaks);
        v.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        TweakList.ItemsSource = v;
        SelRec_Click(null!, null!);
        Loaded += async (_, _) =>
        {
            var i = await Task.Run(SystemInfo.Read);
            TxtCpu.Text = "CPU: " + i.Cpu; TxtGpu.Text = "GPU: " + string.Join(" + ", i.Gpus);
            TxtRam.Text = "RAM: " + i.Ram; TxtOs.Text = i.Os;
            TxtGuide.Text = Guide.Build(i);
            Log($"PC detected. NVIDIA={i.Nvidia} AMD={i.Amd} Intel={i.Intel}");
            string note = Engine.HkcuTargetNote();
            if (!string.IsNullOrEmpty(note)) Log(note);
            // Background real-state detection: each tweak shows “applied / not applied”.
            var states = await Task.Run(() => tweaks.Select(t => (t, applied: Engine.IsApplied(t))).ToList());
            foreach (var (t, applied) in states) t.Status = applied ? "● applied" : "○ not applied";
            Log("Current system state loaded.");
        };
    }

    void Log(string s) { Engine.AppendFileLog(s); TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\n"); TxtLog.ScrollToEnd(); }

    void Nav_Click(object s, RoutedEventArgs e)
    {
        var i = int.Parse((string)((FrameworkElement)s).Tag);
        P0.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        P1.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        P2.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
        P3.Visibility = i == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    void SelRec_Click(object s, RoutedEventArgs e) { foreach (var t in tweaks) t.IsSelected = t.Recommended; }
    void SelAll_Click(object s, RoutedEventArgs e) { foreach (var t in tweaks) t.IsSelected = true; }
    void SelNone_Click(object s, RoutedEventArgs e) { foreach (var t in tweaks) t.IsSelected = false; }

    async Task Busy(Func<Task> work)
    {
        _busy++;
        try
        {
            BtnApply.IsEnabled = BtnRevert.IsEnabled = BtnRevertAll.IsEnabled = false;
            await work();
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); } // async void must never crash: every error goes to the log
        finally { if (--_busy == 0) BtnApply.IsEnabled = BtnRevert.IsEnabled = BtnRevertAll.IsEnabled = true; }
    }

    async void Apply_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    {
        var sel = tweaks.Where(t => t.IsSelected).ToList();
        if (sel.Count == 0) { Log("No tweaks selected."); return; }
        // Laptop + minimum CPU at 100% = more heat/draw: ask for confirmation.
        if (sel.Any(t => t.Name.Contains("Minimum processor state")) && SystemInfo.HasBattery())
        {
            var r = MessageBox.Show("This PC has a battery, and “Minimum processor state at 100%” increases power draw and heat. Apply it anyway?",
                "FrameForge", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes)
            {
                sel.RemoveAll(t => t.Name.Contains("Minimum processor state"));
                Log("Minimum-CPU tweak skipped (battery-powered PC).");
                if (sel.Count == 0) return;
            }
        }
        if (MessageBox.Show("Create a restore point before applying?", "FrameForge", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            Log("Creating restore point…");
            var rp = await Task.Run(Engine.RestorePoint);
            if (rp.ExitCode == 0) Log("Restore point created.");
            else
            {
                Log("Could NOT create the restore point: " + rp.Output);
                if (MessageBox.Show("Could not create the restore point.\n\nContinue WITHOUT that safety net?", "FrameForge", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
        }
        foreach (var t in sel)
        {
            Log("Applying: " + t.Name);
            // Engine.Apply throws if ANY command fails: “✔ applied” is only set then.
            try { await Task.Run(() => Engine.Apply(t, m => Dispatcher.Invoke(() => Log(m)))); t.Status = "✔ applied"; }
            catch (Exception ex) { t.Status = "✖ error"; Log("   " + ex.Message); }
        }
        Log(sel.Any(t => t.Reboot) ? "Done. RESTART the PC to complete all changes." : "Done.");
    });

    async void Revert_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    {
        foreach (var t in tweaks.Where(t => t.IsSelected))
        {
            if (!Engine.HasBackup(t)) { Log("Nothing to revert (FrameForge made no change): " + t.Name); t.Status = "— no changes"; continue; }
            Log("Reverting: " + t.Name);
            try { await Task.Run(() => Engine.Revert(t, m => Dispatcher.Invoke(() => Log(m)))); t.Status = "↩ reverted"; }
            catch (Exception ex) { t.Status = "✖ error"; Log("   " + ex.Message); }
        }
        Log("Revert completed.");
    });

    async void RevertAll_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    {
        int done = 0;
        foreach (var t in tweaks.Where(Engine.HasBackup).ToList())
        {
            Log("Reverting: " + t.Name);
            try { await Task.Run(() => Engine.Revert(t, m => Dispatcher.Invoke(() => Log(m)))); t.Status = "↩ reverted"; done++; }
            catch (Exception ex) { t.Status = "✖ error"; Log("   " + ex.Message); }
        }
        Log(done == 0 ? "No backups to revert." : $"Full revert completed ({done}).");
    });

    async void Restore_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    {
        Log("Creating restore point…");
        var rp = await Task.Run(Engine.RestorePoint);
        Log(rp.ExitCode == 0 ? "Restore point created." : "Could NOT create the restore point: " + rp.Output);
    });

    async void Clean_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    { Log("Cleaning…"); var b = await Task.Run(Engine.Clean); Log($"Freed {b / 1048576.0:0.#} MB and flushed DNS."); });

    void Export_Click(object s, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "FrameForge profile (*.json)|*.json", FileName = "frameforge-profile.json" };
        if (d.ShowDialog() != true) return;
        try
        {
            var profile = tweaks.Select(t => new ProfileItem(t.Name, t.IsSelected)).ToList();
            File.WriteAllText(d.FileName, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Log("Profile exported: " + d.FileName);
        }
        catch (Exception ex) { Log("Could not export the profile: " + ex.Message); }
    }

    void Import_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "FrameForge profile (*.json)|*.json" };
        if (d.ShowDialog() != true) return;
        try
        {
            var profile = JsonSerializer.Deserialize<List<ProfileItem>>(File.ReadAllText(d.FileName)) ?? new();
            int n = 0;
            foreach (var p in profile)
            {
                var t = tweaks.FirstOrDefault(t => t.Name == p.Name);
                if (t != null) { t.IsSelected = p.IsSelected; n++; }
            }
            Log($"Profile imported: {n} of {profile.Count} tweaks recognized.");
        }
        catch (Exception ex) { Log("Could not import the profile: " + ex.Message); }
    }

    async void SelfTest_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    {
        Log("Running engine self-test…");
        bool ok = await Task.Run(() => Engine.SelfTest(m => Dispatcher.Invoke(() => Log(m))));
        MessageBox.Show(ok ? "Self-test passed." : "Self-test found failures: check the log.",
            "FrameForge", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    });

    // ---- Games (previously synchronous on the UI thread: they froze the window) ----
    void Browse_Click(object s, RoutedEventArgs e)
    { var d = new OpenFileDialog { Filter = "Game (*.exe)|*.exe" }; if (d.ShowDialog() == true) TxtExe.Text = d.FileName; }

    async void GameApply_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    {
        string exe = TxtExe.Text.Trim().Trim('"');            // read here, on the UI thread
        bool cpu = ChkIfeo.IsChecked == true;
        if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { Log("Pick a valid .exe."); return; }
        try { await Task.Run(() => Engine.Apply(Engine.GameTweak(exe, cpu), m => Dispatcher.Invoke(() => Log(m)))); Log("Game optimized: " + Path.GetFileName(exe)); }
        catch (Exception ex) { Log("Could not optimize the game: " + ex.Message); }
    });
    async void GameRevert_Click(object s, RoutedEventArgs e) => await Busy(async () =>
    {
        string exe = TxtExe.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(exe)) return;
        var gt = Engine.GameTweak(exe, true);                 // includes every key in case they were applied before
        if (!Engine.HasBackup(gt)) { Log("Nothing to revert: FrameForge never optimized " + Path.GetFileName(exe)); return; }
        try { await Task.Run(() => Engine.Revert(gt, m => Dispatcher.Invoke(() => Log(m)))); Log("Game reverted: " + Path.GetFileName(exe)); }
        catch (Exception ex) { Log("Could not revert the game: " + ex.Message); }
    });

    // ---- Shortcuts ----
    static void Open(string target, Action<string>? log = null)
    { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { log?.Invoke("Could not open '" + target + "': " + ex.Message); } }
    void Web_Click(object s, RoutedEventArgs e) => Open("https://leandrosepulveda.com", Log);
    void GfxSettings_Click(object s, RoutedEventArgs e) => Open("ms-settings:display-advanced-graphics", Log);
    void Nvcp_Click(object s, RoutedEventArgs e)
    {
        // The modern NVIDIA Panel is a Microsoft Store app (URI); the classic one is nvcplui.exe.
        try
        {
            string classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe");
            if (File.Exists(classic)) { Open(classic, Log); return; }
            Open("shell:AppsFolder\\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel", Log);
            Log("If the NVIDIA Panel didn't open, install it from the Microsoft Store (“NVIDIA Control Panel”).");
        }
        catch (Exception ex) { Log("NVIDIA Panel not found: " + ex.Message); }
    }
    void Power_Click(object s, RoutedEventArgs e) => Open("powercfg.cpl", Log);
    void Drivers_Click(object s, RoutedEventArgs e) => Open("devmgmt.msc", Log);
}
