namespace Taskee.Core;

// Coordinates are quantized to 0–1000 to keep the Explorer snapshot small.
// A null Y breaks the line instead of connecting across missing readings.
public sealed record GraphPoint(int X,int? Y);
public sealed record GraphThreshold(int Y,string Color,string Label);
public sealed record HistoryGraph(string Title,string Current,string Top,string Bottom,string Summary,List<GraphPoint> Points,List<string>? Ticks=null,GraphPoint? Latest=null,List<GraphThreshold>? Thresholds=null)
{
    public const int WindowSeconds=300;
    public const int BucketCount=60;
    public const int MaxPoints=BucketCount*4;
    public const int AxisTickCount=5;

    public static HistoryGraph Create(Series series,double now,Measurement reading,StatConfig item,AppearanceConfig? appearance=null)
    {
        var samples=series.Window(now,WindowSeconds);
        var values=samples.Where(p=>p.Value.HasValue).Select(p=>p.Value!.Value).ToList();
        string title=item.Metric=="sensor"&&reading.Description.Length>0?reading.Description:Catalog.Name(item.Metric);
        string current=MetricEngine.Format(reading.Value,reading.Unit,item);
        if(values.Count==0) {series.GraphScale.Reset();return new(title,current,"—","—","Waiting for readings",[]);}

        double low=values.Min(),high=values.Max();
        bool percentage=reading.Unit=="%"&&low>=0&&high<=100;
        double rangeLow=low,rangeHigh=high;
        if(item.Alerts&&!percentage) {
            foreach(double value in new[] {item.Warning,item.Critical}.Where(double.IsFinite)) {
                rangeLow=Math.Min(rangeLow,value);rangeHigh=Math.Max(rangeHigh,value);
            }
        }
        var display=GraphDisplay.Create(reading.Unit,item,Math.Max(Math.Abs(rangeLow),Math.Abs(rangeHigh)));
        var scale=series.GraphScale.Update(display.Convert(rangeLow),display.Convert(rangeHigh),display,
            string.Join('|',reading.Unit,item.Unit,item.Precision,item.Alerts,item.Warning,item.Critical),now,percentage);
        var ticks=Enumerable.Range(0,scale.Count).Select(i=>MetricEngine.Format(display.Raw(scale.Top-i*scale.Step),reading.Unit,item)).ToList();
        int X(double time)=>(int)Math.Round(Math.Clamp((time-(now-WindowSeconds))/WindowSeconds,0,1)*1000);
        int Y(double value)=>(int)Math.Round(Math.Clamp((display.Convert(value)-scale.Bottom)/(scale.Top-scale.Bottom),0,1)*1000);
        var points=new List<GraphPoint>();
        // Preserve extrema and endpoints in each five-second bucket. Keep gaps
        // and the samples after the last gap, so a resumed sensor appears now.
        foreach(var bucket in samples.GroupBy(p=>Math.Min(BucketCount-1,X(p.Time)*BucketCount/1000))) {
            var group=bucket.ToList();
            bool gap=group.Any(p=>!p.Value.HasValue);
            if(gap) {
                points.Add(new(X(group.First(p=>!p.Value.HasValue).Time),null));
                group=group.Skip(group.FindLastIndex(p=>!p.Value.HasValue)+1).ToList();
                if(group.Count==0) continue;
            }
            var selected=gap?new[] {group.MinBy(p=>p.Value),group.MaxBy(p=>p.Value),group[^1]}:
                new[] {group[0],group.MinBy(p=>p.Value),group.MaxBy(p=>p.Value),group[^1]};
            foreach(var sample in selected.Distinct().OrderBy(p=>p.Time)) points.Add(new(X(sample.Time),Y(sample.Value!.Value)));
        }
        string summary="Low "+MetricEngine.Format(low,reading.Unit,item)+"   ·   High "+MetricEngine.Format(high,reading.Unit,item);
        var latest=reading.Value.HasValue&&points.Count>0&&points[^1].Y.HasValue?points[^1]:null;
        var thresholds=new List<GraphThreshold>();
        if(item.Alerts) {
            appearance??=new();
            void Threshold(double value,string color,string name) {
                double converted=display.Convert(value);
                if(double.IsFinite(converted)&&converted>=scale.Bottom&&converted<=scale.Top)
                    thresholds.Add(new(Y(value),color,name+" "+MetricEngine.Format(value,reading.Unit,item)));
            }
            if(item.Warning!=item.Critical) Threshold(item.Warning,appearance.WarningColor,"Warning");
            Threshold(item.Critical,appearance.CriticalColor,"Critical");
        }
        return new(title,current,ticks[0],ticks[^1],summary,points,ticks,latest,thresholds);
    }
}

// Choose round tick values in the units the user sees, not the sensor's units.
internal readonly record struct GraphDisplay(double Factor,double Offset,double MinimumStep)
{
    public double Convert(double value)=>value*Factor+Offset;
    public double Raw(double value)=>(value-Offset)/Factor;
    public static GraphDisplay Create(string unit,StatConfig item,double magnitude)
    {
        double factor=1,offset=0;
        int decimals=item.Precision>=0?item.Precision:unit is "V" or "A"?2:unit is "GB" or "GiB"?1:0;
        if(unit=="°C"&&item.Unit=="f") {factor=1.8;offset=32;}
        if(unit=="B/s") {
            if(item.Unit=="mbps") factor=8/1e6;
            else if(item.Unit=="mib") factor=1d/1048576;
            else {
                double divisor=item.Unit=="binary"?1024:1000;
                for(int i=0;i<3&&magnitude*factor>=divisor;i++) factor/=divisor;
            }
            if(item.Precision<0) decimals=magnitude*factor<10?2:magnitude*factor<100?1:0;
        }
        return new(factor,offset,Math.Pow(10,-decimals));
    }
}

internal readonly record struct GraphRange(double Bottom,double Step,int Count)
{
    public double Top=>Bottom+Step*(Count-1);
}

internal sealed class HistoryScale
{
    internal const int ShrinkDelaySeconds=30;
    private GraphRange? current;
    private string key="";
    private double? shrinkSince;
    private double lastTime;
    private GraphDisplay previousDisplay;
    public void Reset() {current=null;key="";shrinkSince=null;}
    public GraphRange Update(double low,double high,GraphDisplay display,string displayKey,double now,bool percentage)
    {
        if(percentage) return new(0,25,5);
        var candidate=Rounded(low,high,display.MinimumStep);
        // Automatic network units can change when a peak leaves the window.
        // Re-express the existing bounds before applying the shrink delay.
        if(current is GraphRange retained&&key==displayKey&&previousDisplay.Factor!=display.Factor) {
            current=new(display.Convert(previousDisplay.Raw(retained.Bottom)),retained.Step/previousDisplay.Factor*display.Factor,retained.Count);
        }
        if(current==null||key!=displayKey||now<lastTime) {current=candidate;key=displayKey;shrinkSince=null;}
        else if(low<current.Value.Bottom||high>current.Value.Top) {current=candidate;shrinkSince=null;}
        else if(candidate.Top-candidate.Bottom<=(current.Value.Top-current.Value.Bottom)*.65) {
            shrinkSince??=now;
            if(now-shrinkSince>=ShrinkDelaySeconds) {current=candidate;shrinkSince=null;}
        } else shrinkSince=null;
        lastTime=now;
        previousDisplay=display;
        return current.Value;
    }
    private static GraphRange Rounded(double low,double high,double minimumStep)
    {
        double padding=Math.Max((high-low)*.08,Math.Max(Math.Max(Math.Abs(low),Math.Abs(high))*.02,minimumStep*.5));
        double bottom=low>=0?Math.Max(0,low-padding):low-padding,top=high+padding;
        double step=NiceStep(Math.Max((top-bottom)/HistoryGraph.AxisTickCount,minimumStep));
        if(!double.IsFinite(bottom)||!double.IsFinite(top)||!double.IsFinite(step)||step<=0)
            return new(low==high?0:low,Math.Max(Math.Abs(high)/4,minimumStep),5);
        double roundedBottom,roundedTop;
        int intervals;
        do {
            // Integer display precision should also have integer tick steps.
            if(Math.Abs(step/minimumStep-Math.Round(step/minimumStep))>1e-8) step=NiceStep(step*1.01);
            roundedBottom=Math.Floor(bottom/step)*step;roundedTop=Math.Ceiling(top/step)*step;
            intervals=(int)Math.Round((roundedTop-roundedBottom)/step);
            if(intervals>HistoryGraph.AxisTickCount) step=NiceStep(step*1.01);
        } while(intervals>HistoryGraph.AxisTickCount);
        bottom=roundedBottom;
        if(intervals<HistoryGraph.AxisTickCount-1) {
            int extra=HistoryGraph.AxisTickCount-1-intervals;
            bottom-=extra/2*step;
            if(low>=0) bottom=Math.Max(0,bottom);
            intervals=HistoryGraph.AxisTickCount-1;
        }
        return new(bottom,step,intervals+1);
    }
    private static double NiceStep(double value)
    {
        double power=Math.Pow(10,Math.Floor(Math.Log10(value))),scaled=value/power;
        return (scaled<=1?1:scaled<=2?2:scaled<=2.5?2.5:scaled<=5?5:10)*power;
    }
}
