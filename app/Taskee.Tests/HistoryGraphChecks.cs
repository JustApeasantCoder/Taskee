using System.Text;
using System.Text.Json;
using Taskee.Core;

internal static class HistoryGraphChecks
{
    public static void Run(Action<bool,string> check)
    {
        var stat=new StatConfig {Metric="cpu.usage",Label="CPU",Reading="average",WindowSeconds=30};
        var config=new AppConfig {Items=[stat]};
        var engine=new MetricEngine();
        var frame=new SensorFrame {Timestamp=10000,System=new() {["cpu.usage"]=20}};
        engine.Build(config,frame,0,10000);frame.System["cpu.usage"]=80;
        var item=engine.Build(config,frame,10,10000).Columns[0].Items[0];
        check(item.Number==20&&item.History?.Current=="80%","Hover history shows live samples even when the card shows an average");
        check(item.History?.Points[0].X==967&&item.History.Points[^1].X==1000,"History uses elapsed time and leaves uncollected time blank");
        check(item.History?.Top=="100%"&&item.History.Bottom=="0%","Percentage graphs use a stable zero-to-100 scale");
        check(item.History?.Ticks?.SequenceEqual(["100%","75%","50%","25%","0%"])==true,"Percentage grid labels show each quarter of the graph scale");
        check(item.History?.Latest==item.History?.Points[^1],"The latest-reading marker follows the last live sample");
        frame.Timestamp=0;
        var missing=engine.Build(config,frame,20,10000).Columns[0].Items[0];
        check(missing.History?.Current=="—"&&missing.History.Points.Any(p=>p.Y==null),"Stale samples break the history line and clear the live value");
        check(missing.History?.Latest==null,"Unavailable readings do not mark old data as the latest live sample");
        frame.Timestamp=10000;frame.System["cpu.usage"]=35;
        var resumed=engine.Build(config,frame,21,10000).Columns[0].Items[0];
        check(resumed.History?.Points[^1].Y==350&&resumed.History.Points[^2].Y==null,"A resumed sensor appears immediately without bridging its gap");
        config.Paused=true;
        check(engine.Build(config,frame,25,10000).Columns[0].Items[0].History!.Points[^1].Y==null,"Pausing creates a history gap");
        config.Paused=false;frame.System["cpu.usage"]=double.NaN;
        var invalid=engine.Build(config,frame,27,10000).Columns[0].Items[0];
        check(invalid.Number==null&&invalid.History!.Points[^1].Y==null,"Invalid live sensor values remain serializable unavailable samples");
        config.Paused=false;stat.Enabled=false;frame.System["cpu.usage"]=90;engine.Build(config,frame,30,10000);stat.Enabled=true;
        check(engine.Build(config,frame,31,10000).Columns[0].Items[0].History!.Summary.Contains("High 90%"),"History keeps collecting while a card is hidden");
        stat.Reading="sessionPeak";frame.System["cpu.usage"]=40;engine.Build(config,frame,35,10000);engine.ResetPeaks();
        var reset=engine.Build(config,frame,36,10000).Columns[0].Items[0];
        check(reset.Number==40&&reset.History!.Summary.Contains("High 90%"),"Resetting session peaks preserves the hover history");
        stat.Metric="memory.percent";frame.System["memory.percent"]=12;
        check(engine.Build(config,frame,37,10000).Columns[0].Items[0].History!.Points.Count==1,"Changing the metric starts a new history");
        config.Appearance.Tooltips=false;
        check(engine.Build(config,frame,38,10000).Columns[0].Items[0].History==null,"Disabling hover graphs omits their snapshot payload");

        var series=new Series();series.Add(0,1000);series.Add(301,20);series.Add(302,99);series.Add(303,20);
        var graph=HistoryGraph.Create(series,600,new(20,"%","","","Live"),new() {Metric="cpu.usage"});
        check(graph.Points.Any(p=>p.Y==990)&&!graph.Summary.Contains("1000"),"Downsampling keeps a brief spike and excludes expired readings");
        series.Clear();series.Add(1,1.2);
        graph=HistoryGraph.Create(series,1,new(1.2,"V","Board voltage","Test","Live"),new() {Metric="sensor"});
        check(graph.Current=="1.20 V"&&graph.Title=="Board voltage"&&graph.Points.Single().Y==500,"A single constant sensor sample has valid geometry and units");
        var flat=new Series();flat.Add(1,20);
        var flatGraph=HistoryGraph.Create(flat,1,new(20,"W","","","Live"),new() {Metric="cpu.power"});
        check(flatGraph.Top!=flatGraph.Bottom,"Constant whole-number readings still have distinguishable axis labels");
        check(flatGraph.Ticks?.Distinct().Count()==5&&flatGraph.Current=="20 W","Steady readings have five distinct scale labels without changing the current value");
        flat.Clear();flat.Add(1,0);
        var zeroGraph=HistoryGraph.Create(flat,1,new(0,"W","","","Live"),new() {Metric="cpu.power"});
        check(zeroGraph.Ticks?.Distinct().Count()==5&&zeroGraph.Bottom=="0 W","A zero reading keeps the nonnegative scale and distinct intermediate labels");
        series.Add(2,double.NaN);series.Add(3,double.PositiveInfinity);
        graph=HistoryGraph.Create(series,3,new(null,"V","Board voltage","Test","Unavailable"),new() {Metric="sensor"});
        check(graph.Current=="—"&&graph.Points.Any(p=>p.Y==null),"Non-finite readings become graph gaps");
        series.Clear();series.Add(1,0);series.Add(10,100);
        graph=HistoryGraph.Create(series,10,new(100,"°C","CPU","Test","Live"),new() {Metric="cpu.temp",Unit="f",Precision=0});
        check(graph.Current=="212°F"&&graph.Summary.Contains("Low 32°F"),"Graph values and extrema follow Fahrenheit display units");
        check(graph.Ticks?.SequenceEqual(["250°F","200°F","150°F","100°F","50°F","0°F"])==true,"Temperature ticks are rounded in the selected Fahrenheit units");
        series.Clear();series.Add(1,125000);
        graph=HistoryGraph.Create(series,1,new(125000,"B/s","Ethernet","Windows","Live"),new() {Metric="network.down",Unit="mbps",Precision=1});
        check(graph.Current=="1.0 Mbps"&&graph.Summary.Contains("1.0 Mbps"),"Network graphs use the configured byte-to-bit conversion");
        check(graph.Ticks?.Distinct().Count()==5&&graph.Ticks.All(t=>t.EndsWith(" Mbps")),"Network scale labels remain distinct after conversion and rounding");
        series.Clear();graph=HistoryGraph.Create(series,1,new(null,"W","","","Unavailable"),new() {Metric="cpu.power"});
        check(graph.Points.Count==0&&graph.Summary=="Waiting for readings","An empty history clearly waits for readings");

        var temperature=new StatConfig {Metric="cpu.temp",Precision=0};
        var temperatures=new Series();temperatures.Add(0,45);temperatures.Add(1,72);
        var rounded=HistoryGraph.Create(temperatures,1,new(72,"°C","","","Live"),temperature);
        check(rounded.Ticks!.SequenceEqual(["80°C","70°C","60°C","50°C","40°C"]),"Temperature scales use round, evenly spaced tick values");
        temperatures.Clear();temperatures.Add(0,65);
        var steady=HistoryGraph.Create(temperatures,0,new(65,"°C","","","Live"),temperature);
        temperatures.Add(1,66.5);
        var moved=HistoryGraph.Create(temperatures,1,new(66.5,"°C","","","Live"),temperature);
        check(moved.Ticks!.SequenceEqual(steady.Ticks!),"Small changes retain the current scale instead of shifting the graph");
        temperatures.Add(2,90);
        var expanded=HistoryGraph.Create(temperatures,2,new(90,"°C","","","Live"),temperature);
        check(expanded.Top=="100°C"&&expanded.Latest?.Y==750,"A new peak immediately expands the scale and stays inside the graph");
        temperatures.Add(303,65);
        var pending=HistoryGraph.Create(temperatures,303,new(65,"°C","","","Live"),temperature);
        temperatures.Add(332,65);
        var held=HistoryGraph.Create(temperatures,332,new(65,"°C","","","Live"),temperature);
        temperatures.Add(333,65);
        var shrunk=HistoryGraph.Create(temperatures,333,new(65,"°C","","","Live"),temperature);
        check(pending.Ticks!.SequenceEqual(expanded.Ticks!)&&held.Ticks!.SequenceEqual(expanded.Ticks!)&&!shrunk.Ticks!.SequenceEqual(expanded.Ticks!),"An expired peak shrinks the scale only after 30 seconds of a smaller range");
        temperature.Unit="f";
        var changedUnits=HistoryGraph.Create(temperatures,334,new(65,"°C","","","Live"),temperature);
        check(changedUnits.Current=="149°F"&&changedUnits.Ticks!.All(t=>t.EndsWith("°F")),"Changing display units refreshes the retained scale immediately");

        var alertStat=new StatConfig {Metric="cpu.usage",Alerts=true,Warning=80,Critical=95};
        var alertConfig=new AppConfig {Items=[alertStat],Appearance=new() {WarningColor="#FDBA74",CriticalColor="#FDA4AF"}};
        frame.System["cpu.usage"]=61;
        var alerted=new MetricEngine().Build(alertConfig,frame,0,10000).Columns[0].Items[0].History!;
        check(alerted.Thresholds!.SequenceEqual([new(800,"#FDBA74","Warning 80%"),new(950,"#FDA4AF","Critical 95%")]),"Alert lines use underlying threshold values and the configured warning and critical colors");
        temperature.Alerts=true;temperature.Warning=80;temperature.Critical=95;
        var temperatureAlert=HistoryGraph.Create(temperatures,335,new(65,"°C","","","Live"),temperature);
        check(temperatureAlert.Thresholds!.Count==2&&temperatureAlert.Thresholds[0].Label=="Warning 176°F"&&temperatureAlert.Thresholds[1].Label=="Critical 203°F"
            &&temperatureAlert.Thresholds[0].Y<temperatureAlert.Thresholds[1].Y,"Temperature threshold labels convert to Fahrenheit and both thresholds fit the scale");
        temperature.Critical=temperature.Warning;
        check(HistoryGraph.Create(temperatures,336,new(65,"°C","","","Live"),temperature).Thresholds!.Single().Label=="Critical 176°F","Coincident alert thresholds draw one critical line");
        temperature.Alerts=false;
        check(HistoryGraph.Create(temperatures,337,new(65,"°C","","","Live"),temperature).Thresholds!.Count==0,"Disabling alerts removes all threshold lines");

        var networkHistory=new Series();networkHistory.Add(0,2_000_000);networkHistory.Add(1,300_000);
        var networkStat=new StatConfig {Metric="network.down"};
        var networkPeak=HistoryGraph.Create(networkHistory,1,new(300_000,"B/s","","","Live"),networkStat);
        networkHistory.Add(302,300_000);
        var networkPending=HistoryGraph.Create(networkHistory,302,new(300_000,"B/s","","","Live"),networkStat);
        networkHistory.Add(331,300_000);
        var networkHeld=HistoryGraph.Create(networkHistory,331,new(300_000,"B/s","","","Live"),networkStat);
        networkHistory.Add(332,300_000);
        var networkShrunk=HistoryGraph.Create(networkHistory,332,new(300_000,"B/s","","","Live"),networkStat);
        check(networkPending.Ticks!.SequenceEqual(networkPeak.Ticks!)&&networkHeld.Ticks!.SequenceEqual(networkPeak.Ticks!)&&!networkShrunk.Ticks!.SequenceEqual(networkPeak.Ticks!),"Automatic network unit changes preserve the full 30-second scale shrink delay");

        var full=new AppConfig {Items=new(Enumerable.Range(0,32).Select(i=>new StatConfig {Metric="cpu.usage",Label="CPU "+i,Alerts=true}))};
        var fullEngine=new MetricEngine();PanelSnapshot panel=new();
        for(int i=0;i<=1200;i++) {frame.System["cpu.usage"]=20+i%79;panel=fullEngine.Build(full,frame,i*.5,10000);}
        var items=panel.Columns.SelectMany(c=>c.Items).ToList();
        check(items.All(i=>i.History!.Points.Count<=HistoryGraph.MaxPoints&&i.History.Points.All(p=>p.X is >=0 and <=1000&&p.Y is >=0 and <=1000)),"A long session keeps all 32 graphs bounded with valid coordinates");
        var wire=new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        string json=JsonSerializer.Serialize(panel,wire);
        check(Encoding.UTF8.GetByteCount(json)<262144,"All 32 history graphs, latest markers, and alert lines fit the native snapshot size limit");
        var roundtrip=JsonSerializer.Deserialize<PanelSnapshot>(json,wire)!;
        check(roundtrip.Columns[0].Items[0].History!.Points.SequenceEqual(items[0].History!.Points),"History coordinates survive the app-to-native JSON snapshot");
        check(roundtrip.Columns[0].Items[0].History!.Ticks!.SequenceEqual(items[0].History!.Ticks!),"Formatted scale labels survive the app-to-native JSON snapshot");
        string alertJson=JsonSerializer.Serialize(alerted,wire);
        var alertRoundtrip=JsonSerializer.Deserialize<HistoryGraph>(alertJson,wire)!;
        check(alertRoundtrip.Latest==alerted.Latest&&alertRoundtrip.Thresholds!.SequenceEqual(alerted.Thresholds!),"Latest marker and alert lines survive the app-to-native JSON snapshot");
    }
}
