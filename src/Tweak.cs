using System.ComponentModel;
using Microsoft.Win32;
namespace FrameForge;

public enum Risk { Safe, Medium, Advanced }
public record RegOp(string Path, string Name, object Value, RegistryValueKind Kind = RegistryValueKind.DWord);

public class Tweak : INotifyPropertyChanged
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Risk Risk { get; init; }
    public bool Recommended { get; init; }
    public bool Reboot { get; init; }
    public List<RegOp> Reg { get; } = new();
    public List<string> ApplyCmds { get; } = new();
    public List<string> RevertCmds { get; } = new();

    public string RiskText => Risk + (Reboot ? " · restart required" : "");
    bool _sel; public bool IsSelected { get => _sel; set { _sel = value; N(nameof(IsSelected)); } }
    string _st = ""; public string Status { get => _st; set { _st = value; N(nameof(Status)); } }

    public Tweak D(string p, string n, int v) { Reg.Add(new(p, n, v)); return this; }
    // Explicit QWord (DWORDs use D). Used by the self-test and for future 64-bit tweaks.
    public Tweak Q(string p, string n, long v) { Reg.Add(new(p, n, v, RegistryValueKind.QWord)); return this; }
    public Tweak B(string p, string n, byte[] v) { Reg.Add(new(p, n, v, RegistryValueKind.Binary)); return this; }
    public Tweak S(string p, string n, string v) { Reg.Add(new(p, n, v, RegistryValueKind.String)); return this; }
    public Tweak Do(params string[] c) { ApplyCmds.AddRange(c); return this; }
    public Tweak Undo(params string[] c) { RevertCmds.AddRange(c); return this; }

    public event PropertyChangedEventHandler? PropertyChanged;
    void N(string p) => PropertyChanged?.Invoke(this, new(p));
}
