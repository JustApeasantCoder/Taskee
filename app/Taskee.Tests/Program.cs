using Taskee.Core;
using System.Text.Json;

if(args.Contains("--probe")) {
    using var collector=new SensorCollector();
    collector.Sample(); await Task.Delay(1100);
    Console.WriteLine(JsonSerializer.Serialize(collector.Sample(),ConfigStore.Json));
    return;
}
int tests=0;
void Check(bool condition,string name) { if(!condition) throw new Exception(name); tests++; Console.WriteLine("PASS "+name); }
var rate=SensorCollector.CalculateRates(100,200,1100,600,2);
Check(rate.Down==500&&rate.Up==200,"Network byte deltas use elapsed time");
Check(SensorCollector.CalculateRates(100,200,1,2,1).Down==null,"Counter reset returns unavailable, never a negative rate");
Check(SensorCollector.CalculateRates(0,0,100,200,0).Up==null,"Zero elapsed time is rejected");
var series=new Series(); series.Add(0,10); series.Add(2,30); series.Add(6,50);
Check(Math.Abs(series.Read(6,50,"average",30)!.Value-70.0/3)<.001,"Rolling average is weighted by time");
series.Add(8,null);series.Add(12,100);series.Add(14,100);
Check(Math.Abs(series.Read(14,100,"average",30)!.Value-44)<.001,"Missing readings create a gap in averages");
Check(series.Read(14,null,"sessionPeak",30)==null,"Missing current reading does not display an old peak as live");
series.Clear();series.Add(15,3);Check(series.Read(15,3,"sessionPeak",30)==3,"Reset clears session peaks");
var frame=new SensorFrame { Devices=[new("cpu","Test CPU","CPU"),new("intel","Intel UHD","GPU"),new("nvidia","NVIDIA GeForce","GPU")] };
SensorReading Sensor(string id,string kind,double value,string source="Libre Hardware Monitor") => new(id,"cpu","Test CPU","CPU",id,"Temperature",kind,"°C",value,source);
frame.Sensors=[Sensor("package","cpu.package",65),Sensor("core1","cpu.core",60),Sensor("core2","cpu.core",80),Sensor("duplicate","cpu.core",90,"MSI Afterburner"),Sensor("distance","distance",15)];
var stat=new StatConfig { Metric="cpu.temp",CpuSource="package" };
Check(MetricResolver.Resolve(stat,frame).Value==65,"Package temperature stays separate from core sensors");
stat.CpuSource="highest";Check(MetricResolver.Resolve(stat,frame).Value==80,"Hottest core excludes duplicate providers and TjMax distance");
stat.CpuSource="average";Check(MetricResolver.Resolve(stat,frame).Value==70,"Core average uses valid readings from one provider");
frame.Sensors.RemoveAt(0);stat.CpuSource="package";Check(MetricResolver.Resolve(stat,frame).Value==null,"Unavailable package never silently substitutes a core reading");
Check(SensorKinds.Classify("CPU","Temperature","P-Core #1")=="cpu.core","Intel core names are classified");
Check(MetricEngine.Format(.86,"V",new StatConfig())=="0.86 V","Automatic precision preserves fractional voltages");
stat.Unit="f";stat.Precision=0;Check(MetricEngine.Format(0,"°C",stat)=="32°F","Fahrenheit conversion");
stat.Unit="mbps";stat.Precision=1;Check(MetricEngine.Format(125000,"B/s",stat)=="1.0 Mbps","Network bit units convert bytes correctly");
var config=AppConfig.Default();var panel=new MetricEngine().Build(config,frame,1);
Check(panel.Columns.Count==3&&panel.Columns.All(c=>c.Items.Count==2),"Default layout groups paired rows into three columns");
config.Appearance.MaxRows=1;panel=new MetricEngine().Build(config,frame,2);Check(panel.Columns.Count==6,"Single row layout respects row limit");
var roundtrip=JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config,ConfigStore.Json),ConfigStore.Json)!;
ConfigStore.Validate(roundtrip);Check(roundtrip.Items.Count==6&&roundtrip.Items[1].StackWithPrevious,"Profile preserves ordering and stacking");
var legacyMonitorConfig=JsonSerializer.Deserialize<AppConfig>("{\"schemaVersion\":1,\"items\":[]}",ConfigStore.Json)!;
ConfigStore.Validate(legacyMonitorConfig);Check(!legacyMonitorConfig.SecondMonitor&&!legacyMonitorConfig.ThirdMonitor,"Older profiles default to the primary monitor only");
config.SecondMonitor=true;config.ThirdMonitor=true;
var multiMonitor=JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config,ConfigStore.Json),ConfigStore.Json)!;
ConfigStore.Validate(multiMonitor);panel=new MetricEngine().Build(multiMonitor,frame,2);
Check(multiMonitor.SecondMonitor&&multiMonitor.ThirdMonitor&&panel.SecondMonitor&&panel.ThirdMonitor,"Profile roundtrip delivers both additional monitor choices to the native snapshot");
multiMonitor.SecondMonitor=false;panel=new MetricEngine().Build(multiMonitor,frame,3);
Check(!panel.SecondMonitor&&panel.ThirdMonitor,"Third monitor can be enabled independently of the second");
multiMonitor.TaskbarEnabled=false;panel=new MetricEngine().Build(multiMonitor,frame,4);
Check(!panel.Enabled&&panel.ThirdMonitor,"Global display toggle hides all monitors without losing their selection");
roundtrip.Appearance.FontSize=100;ConfigStore.Validate(roundtrip);Check(roundtrip.Appearance.FontSize==20,"Imported font sizes are bounded for the taskbar");
var peakConfig=new AppConfig { Items=[new() {Metric="cpu.usage",Reading="sessionPeak"}] };
var peakEngine=new MetricEngine();frame.System["cpu.usage"]=90;peakEngine.Build(peakConfig,frame,1);
peakConfig.Items[0].Enabled=false;frame.System["cpu.usage"]=60;peakEngine.Build(peakConfig,frame,2);
peakConfig.Items[0].Enabled=true;frame.System["cpu.usage"]=30;
Check(peakEngine.Build(peakConfig,frame,3).Columns[0].Items[0].Number==90,"Hiding a card preserves its session peak");
frame.Timestamp=0;Check(peakEngine.Build(peakConfig,frame,4).Columns[0].Items[0].Number==null,"Stale frames never appear as live values");
string atomic=Path.Combine(Path.GetTempPath(),"Taskee-check-"+Guid.NewGuid().ToString("N")+".json");
try {ConfigStore.AtomicWrite(atomic,"old");ConfigStore.AtomicWrite(atomic,"new",true);Check(File.ReadAllText(atomic)=="new"&&File.ReadAllText(atomic+".bak")=="old","Atomic settings replacement preserves the preceding backup");}
finally {File.Delete(atomic);File.Delete(atomic+".bak");}
var slowConfig=new AppConfig {RefreshMs=5000,Items=[new() {Metric="cpu.usage"}]};
var slowFrame=new SensorFrame {Timestamp=10000,SampleIntervalMs=5000,CollectionDurationMs=750,System=new() {["cpu.usage"]=50}};
var slowEngine=new MetricEngine();
Check(slowEngine.Build(slowConfig,slowFrame,1,15250).Columns[0].Items[0].Number==50,"Five-second refresh tolerates collection overhead instead of displaying dashes");
Check(slowEngine.Build(slowConfig,slowFrame,2,21000).Columns[0].Items[0].Number==null,"Slow refresh still expires genuinely stale data");
slowConfig.RefreshMs=500;
Check(slowEngine.Build(slowConfig,slowFrame,3,16000).Columns[0].Items[0].Number==50,"Refresh edits retain the producer interval until the helper applies them");
slowFrame.SampleIntervalMs=500;slowFrame.CollectionDurationMs=0;
Check(slowEngine.Build(slowConfig,slowFrame,4,15100).Columns[0].Items[0].Number==null,"Fast refresh retains the five-second stale cutoff");
var exactConfig=new StatConfig {Metric="cpu.temp",CpuSource="sensor",Sensor="gpu-temp"};
var exactFrame=new SensorFrame {Sensors=[Sensor("core","cpu.core",70),Sensor("distance","distance",15),
    new("gpu-temp","gpu","GPU","GPU","GPU Core","Temperature","gpu.temp","°C",55,"Test"),
    new("fan","cpu","CPU","CPU","Fan","Fan","other","RPM",1500,"Test"),
    new("voltage","cpu","CPU","CPU","Voltage","Voltage","other","V",1.2,"Test")]};
Check(MetricResolver.Resolve(exactConfig,exactFrame).Value==null,"CPU temperature rejects an imported GPU sensor selection");
exactConfig.Sensor="fan";Check(MetricResolver.Resolve(exactConfig,exactFrame).Value==null,"CPU temperature rejects fan readings");
Check(MetricResolver.SensorChoices(exactConfig,exactFrame).Select(s=>s.Id).SequenceEqual(["core"]),"CPU picker excludes GPU, voltage, fan, and TjMax-distance sensors");
exactConfig.Sensor="core";Check(MetricResolver.Resolve(exactConfig,exactFrame).Value==70,"CPU specific sensor accepts valid CPU temperature");
exactConfig.Metric="sensor";exactConfig.Sensor="voltage";
Check(MetricResolver.Resolve(exactConfig,exactFrame).Value==1.2,"Any hardware sensor still permits voltage readings");
string settingsDirectory=Path.Combine(Path.GetTempPath(),"Taskee-settings-check-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(settingsDirectory);
try {
    string path=Path.Combine(settingsDirectory,"settings.json");
    var fresh=ConfigStore.Open(path);Check(fresh.CanSave&&fresh.IsNew,"Missing settings allow first-run autosave");
    var future=AppConfig.Default();future.SchemaVersion=2;future.Items[0].Label="Future custom profile";
    string original=JsonSerializer.Serialize(future,ConfigStore.Json);File.WriteAllText(path,original);
    var old=AppConfig.Default();old.Items[0].Label="Older backup";string originalBackup=JsonSerializer.Serialize(old,ConfigStore.Json);File.WriteAllText(path+".bak",originalBackup);
    for(int i=0;i<2;i++) {var guarded=ConfigStore.Open(path);var writer=new SettingsAutosave(guarded);writer.Changed(0);writer.TrySave(guarded.Configuration,1000,true);Check(!guarded.CanSave&&writer.Pending&&File.ReadAllText(path)==original&&File.ReadAllText(path+".bak")==originalBackup,"Unsupported profile and its backup survive startup and pending edits "+(i+1));}
    var recoverySession=ConfigStore.Open(path);string archive=recoverySession.Recover(recoverySession.Configuration);
    Check(recoverySession.CanSave&&File.ReadAllText(Path.Combine(archive,"settings.json"))==original&&File.ReadAllText(Path.Combine(archive,"settings.json.bak"))==originalBackup,"Explicit recovery preserves both original files before replacement");
    Check(ConfigStore.Open(path).CanSave&&!ConfigStore.Open(path).IsNew,"Recovered settings load normally on the next launch");
    File.WriteAllText(path,"{corrupt");File.WriteAllText(path+".bak",originalBackup);
    var recovered=ConfigStore.Open(path);Check(!recovered.CanSave&&recovered.Configuration.Items[0].Label=="Older backup"&&File.ReadAllText(path)=="{corrupt","Backup recovery never overwrites the corrupt primary automatically");
    File.WriteAllText(path+".bak","also corrupt");var corrupt=ConfigStore.Open(path);
    Check(!corrupt.CanSave&&corrupt.Notice.Length>0,"Unreadable primary and backup protect the original files and surface a notice");
    File.WriteAllText(path,originalBackup);var normal=ConfigStore.Open(path);var autosave=new SettingsAutosave(normal);
    normal.Configuration.Items[0].Label="Retried setting";autosave.Changed(0);
    Check(!autosave.TrySave(normal.Configuration,449)&&autosave.Pending,"Autosave waits for the edit debounce");
    using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) Check(!autosave.TrySave(normal.Configuration,450)&&autosave.Pending&&autosave.NextAttempt>450,"Locked settings schedule an automatic retry without dropping pending edits");
    Check(autosave.TrySave(normal.Configuration,autosave.NextAttempt)&&!autosave.Pending&&ConfigStore.Open(path).Configuration.Items[0].Label=="Retried setting","Autosave succeeds after the temporary lock is released without another edit");
    normal.Configuration.Items[0].Label="Shared-reader setting";
    using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) normal.Save(normal.Configuration);
    Check(ConfigStore.Open(path).Configuration.Items[0].Label=="Shared-reader setting","Settings reads can coexist with atomic replacement");
} finally {Directory.Delete(settingsDirectory,true);}
HistoryGraphChecks.Run(Check);
Console.WriteLine($"{tests} checks passed.");
