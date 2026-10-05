using System.Windows.Media.Imaging;

namespace Taskee.App;

internal static class HistoryUiChecks
{
    public static void Run(Action<PanelSnapshot> updatePreview,Func<string,TextBlock> target,string directory)
    {
        Directory.CreateDirectory(directory);var checks=new List<string>();
        void Check(bool passed,string description) {if(!passed) throw new InvalidOperationException(description);checks.Add(description);}
        var stat=new StatConfig {Metric="cpu.usage",Label="CPU",Color="#A5B4FC"};
        var config=new AppConfig {Items=[stat]};var engine=new MetricEngine();
        var frame=new SensorFrame {Timestamp=10000,System=new() {["cpu.usage"]=40}};
        var panel=engine.Build(config,frame,0,10000);updatePreview(panel);
        var first=target(stat.Id);var tip=first.ToolTip;
        frame.System["cpu.usage"]=65;panel=engine.Build(config,frame,1,10000);updatePreview(panel);
        Check(ReferenceEquals(first,target(stat.Id))&&ReferenceEquals(tip,first.ToolTip),"Preview retains its hover target and tooltip through value refreshes");
        Check(first.Text.EndsWith("65%"),"Retained preview target shows the refreshed value");
        config.Appearance.Tooltips=false;updatePreview(engine.Build(config,frame,2,10000));
        Check(target(stat.Id).ToolTip==null,"Disabling graphs removes the preview tooltip");

        var series=new Series();
        for(int i=0;i<=600;i++) {
            double value=42+20*Math.Sin(i*.045)+5*Math.Sin(i*.2);
            if(i is 175 or 410) value=96;
            if(i==600) value=61;
            series.Add(i*.5,i is >=260 and <=285?null:value);
        }
        var reading=new Measurement(61,"%","Processor usage","Windows","Live");
        var graph=HistoryGraph.Create(series,300,reading,stat);
        var item=new RenderItem(stat.Id,"CPU","61%",stat.Color,"CPU usage\nProcessor usage\nWindows · Live\nCurrent reading",61,History:graph);
        var view=new HistoryView();view.Update(item);
        Check(view.Children.Count==5,"Hover card contains only the title, current value, graph, time scale, and extrema");
        var plot=view.Children.OfType<Grid>().First();
        Check(plot.Children.OfType<TextBlock>().Select(t=>t.Text).SequenceEqual(["100%","75%","50%","25%","0%"])
            &&plot.Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Line>().Where(l=>l.Y1==l.Y2).Select(l=>l.Y1).SequenceEqual([4d,34d,64d,94d,124d]),
            "Five percentage labels align with evenly spaced horizontal grid lines");
        Check(plot.Height==128&&plot.Children.OfType<TextBlock>().All(t=>t.HorizontalAlignment==HorizontalAlignment.Right&&t.Margin.Right==8),"Taller plot has value labels aligned closely to the graph");
        var time=view.Children.OfType<Grid>().Last().Children.OfType<Canvas>().Single();
        Check(time.Children.OfType<TextBlock>().Select(t=>t.Text).SequenceEqual(["−5m","−4m","−3m","−2m","−1m","Now"]),"Minute labels cover the complete five-minute time scale");
        var marker=plot.Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Ellipse>().Single();
        Check(marker.Width==7&&Canvas.GetLeft(marker)>230,"Latest-reading marker appears at the live end of the line");
        Check(view.Children.OfType<Grid>().First().Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Polyline>().Count()==2,"Rendered graph has separate lines on each side of a missing-data gap");
        Capture(view,"history-usage.png",directory);
        stat.Alerts=true;stat.Warning=80;stat.Critical=95;
        view.Update(item with {History=HistoryGraph.Create(series,300,reading,stat)});
        var alertLines=view.Children.OfType<Grid>().First().Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Line>().Where(l=>l.StrokeDashArray.Count>0).ToList();
        Check(alertLines.Count==2&&alertLines[0].Y1==28&&alertLines[1].Y1==10,"Dashed warning and critical lines match their scale positions");
        Capture(view,"history-alerts.png",directory);
        series.Add(301,null);
        view.Update(item with {History=HistoryGraph.Create(series,301,reading with {Value=null},stat)});
        Check(!view.Children.OfType<Grid>().First().Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Ellipse>().Any(t=>t.Width==7),"Missing live readings clear the latest-reading marker");
        view.Update(item with {History=HistoryGraph.Create(new Series(),300,new(null,"%","","","Unavailable"),stat)});
        Check(view.Children.OfType<TextBlock>().Any(t=>t.Text=="Waiting for readings"),"Empty graph shows its waiting state");Capture(view,"history-empty.png",directory);
        series.Clear();series.Add(300,1.2);
        var voltage=new StatConfig {Metric="sensor",Color="#67E8F9"};
        view.Update(item with {Color=voltage.Color,Tooltip="Any hardware sensor\nMotherboard · Voltage\nTest provider · Live\nCurrent reading",History=HistoryGraph.Create(series,300,new(1.2,"V","Motherboard · Voltage","Test provider","Live"),voltage)});
        Capture(view,"history-voltage.png",directory);
        Check(view.Children.OfType<Grid>().First().Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Ellipse>().Count()==1,"Single-sample graph draws a visible dot");
        view.Update(item with {History=graph with {Ticks=[]}});
        Check(view.Children.OfType<Grid>().First().Children.OfType<Canvas>().Single().Children.OfType<System.Windows.Shapes.Line>().Count(l=>l.Y1==l.Y2)==5,"Incomplete legacy tick lists retain a valid graph grid");
        File.WriteAllText(Path.Combine(directory,"history-ui-checks.json"),JsonSerializer.Serialize(new {Passed=true,Checks=checks},new JsonSerializerOptions {WriteIndented=true}));
        Program.Log("History UI checks: "+checks.Count+" passed");
    }
    private static void Capture(HistoryView view,string name,string directory)
    {
        var border=new Border {Child=view,Background=HistoryToolTip.Brush("#1A202C"),BorderBrush=HistoryToolTip.Brush("#506077"),BorderThickness=new Thickness(1),Padding=new Thickness(14),CornerRadius=new CornerRadius(8)};
        border.Measure(new Size(360,1000));border.Arrange(new Rect(new Point(),border.DesiredSize));border.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(border.ActualWidth),(int)Math.Ceiling(border.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(border);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name));png.Save(file);
        border.Child=null;
    }
}
