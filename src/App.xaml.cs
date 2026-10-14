using System.Text;
using System.Windows;

namespace FrameForge;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Headless self-test (handy after building or for diagnosing another PC): FrameForge.exe --selftest
        if (e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var sb = new StringBuilder();
            Engine.Load();
            bool ok;
            // The result also goes to the %ProgramData%\FrameForge\logs log (works in headless sessions).
            void Both(string m) { sb.AppendLine(m); Engine.AppendFileLog("--selftest: " + m); }
            try { ok = Engine.SelfTest(Both); }
            catch (Exception ex) { ok = false; Both("EXCEPTION: " + ex.Message); }
            MessageBox.Show(sb.ToString(), ok ? "FrameForge · self-test passed" : "FrameForge · self-test found failures",
                MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Shutdown();
            return;
        }
        new MainWindow().Show();
    }
}
