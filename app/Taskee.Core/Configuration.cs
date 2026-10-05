using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Taskee.Core;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new(name)); return true;
    }
    public void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class StatConfig : Observable
{
    private string id = Guid.NewGuid().ToString("N"), metric = "cpu.temp", label = "CPU", device = "auto", sensor = "", cpuSource = "auto", reading = "current", unit = "auto", color = "#D8E3EF";
    private bool enabled = true, stack, alerts;
    private int window = 30, precision = -1;
    private double warn = 80, critical = 95;
    public string Id { get => id; set => Set(ref id, value); }
    public string Metric { get => metric; set => Set(ref metric, value); }
    public string Label { get => label; set => Set(ref label, value); }
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public bool StackWithPrevious { get => stack; set => Set(ref stack, value); }
    public string Device { get => device; set => Set(ref device, value); }
    public string Sensor { get => sensor; set => Set(ref sensor, value); }
    public string CpuSource { get => cpuSource; set => Set(ref cpuSource, value); }
    public string Reading { get => reading; set => Set(ref reading, value); }
    public int WindowSeconds { get => window; set => Set(ref window, value); }
    public string Unit { get => unit; set => Set(ref unit, value); }
    public int Precision { get => precision; set => Set(ref precision, value); }
    public string Color { get => color; set => Set(ref color, value); }
    public bool Alerts { get => alerts; set => Set(ref alerts, value); }
    public double Warning { get => warn; set => Set(ref warn, value); }
    public double Critical { get => critical; set => Set(ref critical, value); }
}

public sealed class AppearanceConfig : Observable
{
    private string font = "Segoe UI", weight = "Medium", background = "#00000000", warning = "#FBBF24", critical = "#FB7185";
    private double fontSize = 11, gap = 18, rowGap = 1, padding = 10, opacity = 1, radius = 5;
    private int rows = 2, minWidth = 0, maxWidth = 620;
    private bool labels = true, separators, fixedWidths = true, tooltip = true;
    public string Font { get => font; set => Set(ref font, value); }
    public string Weight { get => weight; set => Set(ref weight, value); }
    public double FontSize { get => fontSize; set => Set(ref fontSize, value); }
    public double ColumnGap { get => gap; set => Set(ref gap, value); }
    public double RowGap { get => rowGap; set => Set(ref rowGap, value); }
    public double Padding { get => padding; set => Set(ref padding, value); }
    public double Opacity { get => opacity; set => Set(ref opacity, value); }
    public double Radius { get => radius; set => Set(ref radius, value); }
    public string Background { get => background; set => Set(ref background, value); }
    public string WarningColor { get => warning; set => Set(ref warning, value); }
    public string CriticalColor { get => critical; set => Set(ref critical, value); }
    public int MaxRows { get => rows; set => Set(ref rows, value); }
    public int MinimumWidth { get => minWidth; set => Set(ref minWidth, value); }
    public int MaximumWidth { get => maxWidth; set => Set(ref maxWidth, value); }
    public bool ShowLabels { get => labels; set => Set(ref labels, value); }
    public bool Separators { get => separators; set => Set(ref separators, value); }
    public bool FixedWidths { get => fixedWidths; set => Set(ref fixedWidths, value); }
    public bool Tooltips { get => tooltip; set => Set(ref tooltip, value); }
}

public sealed class AppConfig : Observable
{
    public int SchemaVersion { get; set; } = 1;
    public ObservableCollection<StatConfig> Items { get; set; } = [];
    public AppearanceConfig Appearance { get; set; } = new();
    private bool taskbar = true, tray = true, notifications, paused, afterburner = true, secondMonitor, thirdMonitor;
    private int interval = 1000;
    public bool TaskbarEnabled { get => taskbar; set => Set(ref taskbar, value); }
    public bool SecondMonitor { get => secondMonitor; set => Set(ref secondMonitor, value); }
    public bool ThirdMonitor { get => thirdMonitor; set => Set(ref thirdMonitor, value); }
    public bool CloseToTray { get => tray; set => Set(ref tray, value); }
    public bool Notifications { get => notifications; set => Set(ref notifications, value); }
    public bool Paused { get => paused; set => Set(ref paused, value); }
    public bool AfterburnerBridge { get => afterburner; set => Set(ref afterburner, value); }
    public int RefreshMs { get => interval; set => Set(ref interval, value); }
    public static AppConfig Default() => new() { Items = [
        new() { Metric="cpu.temp", Label="CPU", Color="#A5B4FC" },
        new() { Metric="cpu.power", Label="CPU", StackWithPrevious=true, Color="#A5B4FC" },
        new() { Metric="gpu.temp", Label="GPU", Color="#67E8F9" },
        new() { Metric="gpu.power", Label="GPU", StackWithPrevious=true, Color="#67E8F9" },
        new() { Metric="network.down", Label="↓", Color="#6EE7B7" },
        new() { Metric="network.up", Label="↑", StackWithPrevious=true, Color="#6EE7B7" }
    ] };
}

public sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
public static class Catalog
{
    public static readonly Choice[] Metrics = [new("cpu.temp","CPU temperature"), new("cpu.power","CPU power"), new("gpu.temp","GPU temperature"), new("gpu.power","GPU power"), new("network.down","Download speed"), new("network.up","Upload speed"), new("cpu.usage","CPU usage"), new("gpu.usage","GPU usage"), new("memory.percent","Memory usage"), new("memory.used","Memory used"), new("gpu.memory","GPU memory used"), new("sensor","Any hardware sensor")];
    public static readonly Choice[] CpuSources = [new("auto","Automatic"), new("package","Package temperature"), new("highest","Highest core temperature"), new("average","Average of core sensors"), new("sensor","Specific sensor")];
    public static readonly Choice[] Readings = [new("current","Current reading"), new("average","Rolling average"), new("peak","Rolling peak"), new("minimum","Rolling minimum"), new("sessionPeak","Session peak")];
    public static readonly Choice[] Units = [new("auto","Automatic units"),new("c","°C"),new("f","°F"),new("bytes","Auto B/s · decimal"),new("binary","Auto B/s · binary"),new("mbps","Mbps"),new("mib","MiB/s")];
    public static string Label(string metric) => metric.StartsWith("cpu") ? "CPU" : metric.StartsWith("gpu") ? "GPU" : metric=="network.down" ? "↓" : metric=="network.up" ? "↑" : metric.StartsWith("memory") ? "RAM" : "Sensor";
    public static string Name(string metric) => Metrics.FirstOrDefault(m=>m.Id==metric)?.Name ?? metric;
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, WriteIndented=true };
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Taskee");
    public static string FilePath => Path.Combine(DirectoryPath,"settings.json");
    public static AppConfig Load() => Open().Configuration;
    public static SettingsSession Open(string? path=null) => new(path??FilePath);
    internal static AppConfig Read(string path)
    {
        // Atomic replacement must remain possible while the sensor helper reads.
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(file.Length>1_000_000) throw new InvalidDataException("The settings file is too large.");
        return JsonSerializer.Deserialize<AppConfig>(file,Json)??throw new InvalidDataException("The settings file is empty.");
    }
    internal static void Save(AppConfig config,string path)
    {
        Validate(config); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicWrite(path,JsonSerializer.Serialize(config,Json),true);
    }
    public static void AtomicWrite(string path,string content,bool backup=false)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllText(temporary,content,new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary,path,backup?path+".bak":null);
            else File.Move(temporary,path);
        } finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Validate(AppConfig config)
    {
        if(config.SchemaVersion!=1) throw new InvalidDataException("This profile uses an unsupported settings version.");
        if(config.Items==null || config.Appearance==null || config.Items.Count>32) throw new InvalidDataException("A profile can contain up to 32 stat cards.");
        var ids=new HashSet<string>();
        foreach(var item in config.Items) {
            if(item==null) throw new InvalidDataException("A stat card cannot be null.");
            if(!Catalog.Metrics.Any(m=>m.Id==item.Metric)) throw new InvalidDataException("Unknown stat: "+item.Metric);
            if(!Catalog.Readings.Any(m=>m.Id==item.Reading) || !Catalog.CpuSources.Any(m=>m.Id==item.CpuSource) || !Catalog.Units.Any(m=>m.Id==item.Unit)) throw new InvalidDataException("Unknown reading mode or units.");
            if(string.IsNullOrWhiteSpace(item.Id) || item.Id.Length>64 || !ids.Add(item.Id)) { item.Id=Guid.NewGuid().ToString("N"); ids.Add(item.Id); }
            item.Label=(item.Label??"")[..Math.Min(24,(item.Label??"").Length)];
            item.Label=Regex.Replace(item.Label,@"[\r\n\t]"," ");
            item.Device??="auto"; item.Sensor??="";
            item.WindowSeconds=Math.Clamp(item.WindowSeconds,2,600); item.Precision=Math.Clamp(item.Precision,-1,2);
            item.Color=Hex(item.Color,"#D8E3EF");
            if(!double.IsFinite(item.Warning) || !double.IsFinite(item.Critical)) { item.Warning=80; item.Critical=95; }
            if(item.Critical<item.Warning) item.Critical=item.Warning;
        }
        var a=config.Appearance;
        a.Font=string.IsNullOrWhiteSpace(a.Font)?"Segoe UI":a.Font[..Math.Min(a.Font.Length,100)];
        if(a.Weight is not ("Normal" or "Medium" or "SemiBold" or "Bold")) a.Weight="Medium";
        a.FontSize=Clamp(a.FontSize,8,20,11); a.ColumnGap=Clamp(a.ColumnGap,0,48,18); a.RowGap=Clamp(a.RowGap,0,8,1);
        a.Padding=Clamp(a.Padding,0,24,10); a.Opacity=Clamp(a.Opacity,.25,1,1); a.Radius=Clamp(a.Radius,0,16,5);
        a.MaxRows=Math.Clamp(a.MaxRows,1,3); a.MinimumWidth=Math.Clamp(a.MinimumWidth,0,600);
        a.MaximumWidth=Math.Clamp(a.MaximumWidth,Math.Max(100,a.MinimumWidth),1200);
        a.Background=Hex(a.Background,"#00000000"); a.WarningColor=Hex(a.WarningColor,"#FBBF24"); a.CriticalColor=Hex(a.CriticalColor,"#FB7185");
        config.RefreshMs=Math.Clamp(config.RefreshMs,500,5000);
    }
    private static double Clamp(double v,double min,double max,double fallback)=>double.IsFinite(v)?Math.Clamp(v,min,max):fallback;
    private static string Hex(string? color,string fallback)=>Regex.IsMatch(color??"", "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")?color!:fallback;
}
