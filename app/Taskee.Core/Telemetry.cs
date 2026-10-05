using System.Globalization;
using System.Text.RegularExpressions;

namespace Taskee.Core;

public sealed record SensorReading(string Id,string DeviceId,string DeviceName,string Group,string Name,string Type,string Kind,string Unit,double? Value,string Source);
public sealed record DeviceInfo(string Id,string Name,string Group);
public sealed record AdapterInfo(string Id,string Name,bool Physical,bool Up);
public sealed class SensorFrame
{
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public List<SensorReading> Sensors { get; set; } = [];
    public List<DeviceInfo> Devices { get; set; } = [];
    public List<AdapterInfo> Adapters { get; set; } = [];
    public Dictionary<string,double?> System { get; set; } = [];
    public string Status { get; set; } = "Starting sensors";
    public bool DriverInstalled { get; set; }
    public bool Elevated { get; set; }
    public int SampleIntervalMs { get; set; } = 1000;
    public long CollectionDurationMs { get; set; }
}
public sealed record Measurement(double? Value,string Unit,string Description,string Source,string Status);
public sealed record RenderItem(string Id,string Label,string Value,string Color,string Tooltip,double? Number,string WidthHint="",HistoryGraph? History=null);
public sealed record RenderColumn(List<RenderItem> Items);
public sealed class PanelSnapshot
{
    public int Version { get; set; } = 1;
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public bool Enabled { get; set; }
    public bool SecondMonitor { get; set; }
    public bool ThirdMonitor { get; set; }
    public bool Paused { get; set; }
    public AppearanceConfig Appearance { get; set; } = new();
    public List<RenderColumn> Columns { get; set; } = [];
    public string LayoutKey { get; set; } = "";
}

public static class SensorKinds
{
    public static string Classify(string group,string type,string name)
    {
        string n=name.ToLowerInvariant();
        if(group=="CPU" && type=="Temperature") {
            if(n.Contains("distance") || n.Contains("tjmax")) return "distance";
            if(n.Contains("package") || n.Contains("tctl") || n is "cpu (tdie)") return "cpu.package";
            if(n is "core max" or "core maximum") return "cpu.highest";
            if(n is "core average") return "cpu.average";
            if(Regex.IsMatch(n,@"(p-core|e-core|cpu core|core)\s*#?\d+|^cpu\d+ temperature")) return "cpu.core";
            return "cpu.generic";
        }
        if(group=="CPU" && type=="Power") return n.Contains("package") || n=="cpu power" ? "cpu.power" : "other";
        if(group=="GPU" && type=="Temperature") return n.Contains("hot spot") || n.Contains("hotspot") ? "gpu.hotspot" : n.Contains("memory") ? "gpu.memorytemp" : "gpu.temp";
        if(group=="GPU" && type=="Power") return n.Contains("package") || n is "gpu power" or "power" or "gpu total" or "gpu board power" ? "gpu.power" : "other";
        if(group=="GPU" && type=="Load" && (n.Contains("core") || n.Contains("gpu usage"))) return "gpu.usage";
        if(group=="GPU" && type=="SmallData" && (n.Contains("memory used") || n.Contains("memory usage"))) return "gpu.memory";
        return "other";
    }
}

public static class MetricResolver
{
    public static IEnumerable<SensorReading> SensorChoices(StatConfig item,SensorFrame frame)
        => frame.Sensors.Where(s=>s.Unit.Length>0 && (item.Metric!="cpu.temp" ||
            (s.Group=="CPU" && s.Type=="Temperature" && s.Unit=="°C" && s.Kind!="distance" &&
             (item.Device=="auto" || s.DeviceId==item.Device))));
    public static Measurement Resolve(StatConfig item,SensorFrame frame)
    {
        string metric=item.Metric;
        if(metric.StartsWith("network.")) {
            string adapter=item.Device;
            if(adapter=="auto") adapter=frame.Adapters.FirstOrDefault(a=>a.Physical&&a.Up)?.Id??"";
            string key=metric+":"+adapter;
            var value=frame.System.GetValueOrDefault(key);
            return new(value,"B/s",frame.Adapters.FirstOrDefault(a=>a.Id==adapter)?.Name??"No connected adapter","Windows",value.HasValue?"Live":"Waiting for network counters");
        }
        if(metric is "cpu.usage" or "memory.percent" or "memory.used") {
            var v=frame.System.GetValueOrDefault(metric);
            return new(v,metric=="memory.used"?"GiB":"%",Catalog.Name(metric),"Windows",v.HasValue?"Live":"Waiting for system counters");
        }
        if(metric=="sensor" || (metric=="cpu.temp"&&item.CpuSource=="sensor")) {
            var exact=SensorChoices(item,frame).FirstOrDefault(s=>s.Id==item.Sensor);
            return FromSensor(exact,metric=="cpu.temp"?"Choose a CPU temperature sensor":"Choose a hardware sensor");
        }
        string group=metric.StartsWith("cpu")?"CPU":"GPU";
        var devices=frame.Devices.Where(d=>d.Group==group).ToList();
        string? device=item.Device=="auto" ? devices.OrderBy(d=>group=="GPU" && d.Name.Contains("Intel",StringComparison.OrdinalIgnoreCase)?1:0).FirstOrDefault()?.Id : item.Device;
        var sensors=frame.Sensors.Where(s=>s.Group==group&&s.DeviceId==device&&s.Value.HasValue).ToList();
        if(metric=="cpu.temp") {
            string mode=item.CpuSource;
            var package=Best(sensors,"cpu.package"); var cores=CoreSet(sensors);
            if(mode=="auto") {
                if(package!=null) return FromSensor(package,"");
                if(cores.Count>0) return new(cores.Max(s=>s.Value),"°C","Highest reported core temperature",cores[0].Source,"Live");
                return FromSensor(Best(sensors,"cpu.generic"),"CPU temperature needs sensor access");
            }
            if(mode=="package") return FromSensor(package,"Package temperature is unavailable");
            if(cores.Count>0) return new(mode=="highest"?cores.Max(s=>s.Value):cores.Average(s=>s.Value!.Value),"°C",mode=="highest"?"Highest reported core temperature":"Average of reported core sensors",cores[0].Source,"Live");
            return FromSensor(Best(sensors,mode=="highest"?"cpu.highest":"cpu.average"),"Core temperatures are unavailable");
        }
        string kind=metric switch {"cpu.power"=>"cpu.power","gpu.temp"=>"gpu.temp","gpu.power"=>"gpu.power","gpu.usage"=>"gpu.usage","gpu.memory"=>"gpu.memory",_=>"unknown"};
        var result=FromSensor(Best(sensors,kind),Catalog.Name(metric)+" is unavailable");
        return kind=="gpu.memory"&&result.Value.HasValue ? result with {Value=result.Value/1024,Unit="GiB"} : result;
    }
    private static List<SensorReading> CoreSet(List<SensorReading> sensors) {
        var cores=sensors.Where(s=>s.Kind=="cpu.core").ToList();
        var primary=cores.Where(s=>s.Source=="Libre Hardware Monitor").ToList();
        return primary.Count>0?primary:cores;
    }
    private static SensorReading? Best(List<SensorReading> sensors,string kind)=>sensors.Where(s=>s.Kind==kind).OrderBy(s=>s.Source=="Libre Hardware Monitor"?0:1).FirstOrDefault();
    private static Measurement FromSensor(SensorReading? s,string unavailable)=>s==null?new(null,"","", "",unavailable):new(s.Value,s.Unit,s.DeviceName+" · "+s.Name,s.Source,s.Value.HasValue?"Live":"Sensor has no current reading");
}

public sealed class Series
{
    private readonly List<(double Time,double? Value)> points=[];
    private double? peak;
    internal HistoryScale GraphScale { get; }=new();
    public string Unit { get; private set; }="";
    public void Add(double time,double? value,string unit="")
    {
        if(unit.Length>0) Unit=unit;
        if(value.HasValue && !double.IsFinite(value.Value)) value=null;
        if(points.Count>0 && time<=points[^1].Time) return;
        points.Add((time,value));
        if(value.HasValue) peak=peak.HasValue?Math.Max(peak.Value,value.Value):value;
        while(points.Count>2 && points[1].Time<time-600) points.RemoveAt(0);
    }
    public double? Read(double now,double? current,string mode,int seconds)
    {
        if(!current.HasValue) return null;
        if(mode=="current") return current;
        if(mode=="sessionPeak") return peak;
        double start=now-seconds;
        var active=points.Where(p=>p.Time>=start&&p.Time<=now&&p.Value.HasValue).ToList();
        if(mode=="peak") return active.Count>0?active.Max(p=>p.Value):current;
        if(mode=="minimum") return active.Count>0?active.Min(p=>p.Value):current;
        double total=0,duration=0;
        for(int i=0;i<points.Count;i++) {
            double left=Math.Max(start,points[i].Time),right=Math.Min(now,i+1<points.Count?points[i+1].Time:now);
            if(right>left && points[i].Value.HasValue) { total+=(right-left)*points[i].Value!.Value; duration+=right-left; }
        }
        return duration>0?total/duration:current;
    }
    public void Clear() { points.Clear(); peak=null; Unit=""; GraphScale.Reset(); }
    public void ResetPeak()=>peak=points.LastOrDefault().Value;
    public IReadOnlyList<(double Time,double? Value)> Window(double now,int seconds)
        => points.Where(p=>p.Time>=now-seconds&&p.Time<=now).ToList();
}

public sealed class MetricEngine
{
    private readonly Dictionary<string,Series> histories=[];
    public Dictionary<string,Measurement> Last { get; }=[];
    public void Reset()=>histories.Clear();
    public void ResetPeaks() { foreach(var history in histories.Values) history.ResetPeak(); }
    public PanelSnapshot Build(AppConfig config,SensorFrame frame,double time,long? utcNow=null)
    {
        var panel=new PanelSnapshot {Enabled=config.TaskbarEnabled,SecondMonitor=config.SecondMonitor,ThirdMonitor=config.ThirdMonitor,Paused=config.Paused,Appearance=config.Appearance};
        long age=(utcNow??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())-frame.Timestamp;
        long freshness=Math.Max(5000,2L*Math.Max(Math.Clamp(config.RefreshMs,500,5000),Math.Clamp(frame.SampleIntervalMs,500,5000))+Math.Clamp(frame.CollectionDurationMs,0,5000));
        bool stale=frame.Timestamp<=0 || age>freshness || age < -5000;
        Last.Clear();
        var valid=new HashSet<string>();
        foreach(var item in config.Items) {
            var reading=MetricResolver.Resolve(item,frame);
            if(reading.Value.HasValue&&!double.IsFinite(reading.Value.Value)) reading=reading with {Value=null,Status="Sensor has no current reading"};
            if(config.Paused) reading=reading with {Value=null,Status="Monitoring paused"};
            if(stale) reading=reading with {Value=null,Status="Waiting for fresh sensor data"};
            Last[item.Id]=reading;
            string key=string.Join('|',item.Id,item.Metric,item.Device,item.Sensor,item.CpuSource);
            valid.Add(key);
            if(!histories.TryGetValue(key,out var history)) histories[key]=history=new();
            history.Add(time,reading.Value,reading.Unit);
            var value=history.Read(time,reading.Value,item.Reading,item.WindowSeconds);
            if(!item.Enabled) continue;
            string color=item.Color;
            if(item.Alerts&&value.HasValue) color=value>=item.Critical?config.Appearance.CriticalColor:value>=item.Warning?config.Appearance.WarningColor:color;
            string mode=Catalog.Readings.FirstOrDefault(c=>c.Id==item.Reading)?.Name??item.Reading;
            string tooltip=Catalog.Name(item.Metric)+"\n"+reading.Description+"\n"+reading.Source+" · "+reading.Status+"\n"+mode+(item.Reading is "average" or "peak" or "minimum"?$" · {item.WindowSeconds}s":"");
            string hint=reading.Unit switch { "°C"=>item.Unit=="f"?"999°F":"999°C","W"=>"999.9 W","%"=>"100%","B/s"=>item.Unit=="mbps"?"999.9 Mbps":item.Unit is "binary" or "mib"?"999.9 MiB/s":"999.9 MB/s","GiB"=>"99.9 GiB",_=>"999.9 "+reading.Unit };
            var graph=config.Appearance.Tooltips?HistoryGraph.Create(history,time,reading with {Unit=history.Unit},item,config.Appearance):null;
            var rendered=new RenderItem(item.Id,config.Appearance.ShowLabels?item.Label:"",Format(value,reading.Unit,item),color,tooltip,value,hint,graph);
            if(!item.StackWithPrevious || panel.Columns.Count==0 || panel.Columns[^1].Items.Count>=config.Appearance.MaxRows) panel.Columns.Add(new([]));
            panel.Columns[^1].Items.Add(rendered);
        }
        var definitions=config.Items.ToDictionary(i=>i.Id);
        panel.LayoutKey=string.Join(';',panel.Columns.Select(c=>string.Join(',',c.Items.Select(i=> {
            var d=definitions[i.Id];return string.Join('|',d.Id,d.Metric,d.Label,d.Unit,d.Precision,d.Device,d.Sensor,d.CpuSource);
        }))))+JsonAppearance(config.Appearance);
        foreach(var key in histories.Keys.Where(k=>!valid.Contains(k)).ToList()) histories.Remove(key);
        return panel;
    }
    private static string JsonAppearance(AppearanceConfig a)=>System.Text.Json.JsonSerializer.Serialize(a,ConfigStore.Json);
    public static string Format(double? value,string unit,StatConfig item)
    {
        if(!value.HasValue || !double.IsFinite(value.Value)) return "—";
        double number=value.Value; string suffix=unit;
        int decimals=item.Precision>=0?item.Precision:unit is "V" or "A"?2:unit is "°C" or "%" or "RPM"?0:unit is "GB" or "GiB"?1:0;
        if(unit=="°C" && item.Unit=="f") { number=number*1.8+32; suffix="°F"; }
        if(unit=="B/s") {
            if(item.Unit=="mbps") { number=number*8/1e6; suffix="Mbps"; }
            else if(item.Unit=="mib") { number/=1048576; suffix="MiB/s"; }
            else {
                double divisor=item.Unit=="binary"?1024:1000; string[] units=item.Unit=="binary"?["B/s","KiB/s","MiB/s","GiB/s"]:["B/s","KB/s","MB/s","GB/s"];
                int i=0; while(number>=divisor&&i<3) {number/=divisor;i++;} suffix=units[i];
            }
            if(item.Precision<0) decimals=number<10?2:number<100?1:0;
        }
        string formatted=number.ToString("F"+decimals,CultureInfo.InvariantCulture);
        return formatted+(suffix is "°C" or "°F" or "%"?suffix:" "+suffix);
    }
}
