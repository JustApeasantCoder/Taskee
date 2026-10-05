using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace Taskee.App;

internal sealed class HistoryToolTip : ToolTip
{
    private RenderItem? item;
    private readonly HistoryView view=new();
    public HistoryToolTip()
    {
        Placement=PlacementMode.Top;Content=view;Padding=new Thickness(14);
        Background=Brush("#1A202C");Foreground=Brush("#ECF0F8");BorderBrush=Brush("#506077");
        Opened+=(s,e)=> {if(item!=null) view.Update(item);};
    }
    public void Update(RenderItem value) {item=value;if(IsOpen) view.Update(value);}
    public static void Set(FrameworkElement target,RenderItem item,bool enabled)
    {
        if(!enabled) {if(target.ToolTip is ToolTip old) old.IsOpen=false;target.ToolTip=null;return;}
        if(target.ToolTip is not HistoryToolTip tip) {
            tip=new();target.ToolTip=tip;
            ToolTipService.SetInitialShowDelay(target,400);ToolTipService.SetShowDuration(target,int.MaxValue);
        }
        tip.Update(item);
    }
    internal static Brush Brush(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}

internal sealed class HistoryView : StackPanel
{
    private const double PlotWidth=246,PlotHeight=128,PlotPadding=4;
    public HistoryView() {Width=320;}
    private static TextBlock Label(string text,double size=11,string color="#98A4B9")=>new() {
        Text=text,FontFamily=new("Segoe UI"),FontSize=size,Foreground=HistoryToolTip.Brush(color),TextWrapping=TextWrapping.Wrap
    };
    public void Update(RenderItem item)
    {
        Children.Clear();var graph=item.History;
        if(graph==null) {Children.Add(Label(item.Tooltip));return;}
        Children.Add(Label(graph.Title,13,"#ECF0F8"));
        var current=Label(graph.Current,23,item.Color);current.Margin=new Thickness(0,4,0,8);Children.Add(current);
        var plot=new Grid {Height=PlotHeight};
        plot.ColumnDefinitions.Add(new() {Width=new GridLength(74)});plot.ColumnDefinitions.Add(new() {Width=new GridLength(PlotWidth)});
        bool hasTicks=graph.Ticks is {Count:>=2 and <=9};
        IReadOnlyList<string> ticks=hasTicks?graph.Ticks!:[graph.Top,graph.Bottom];
        for(int i=0;i<ticks.Count;i++) {
            var label=Label(ticks[i]);
            label.HorizontalAlignment=HorizontalAlignment.Right;label.TextAlignment=TextAlignment.Right;
            label.VerticalAlignment=i==ticks.Count-1?VerticalAlignment.Bottom:VerticalAlignment.Top;
            double top=i>0&&i<ticks.Count-1?PlotPadding+i*(PlotHeight-2*PlotPadding)/(ticks.Count-1)-7:0;
            label.Margin=new Thickness(0,top,8,0);
            plot.Children.Add(label);
        }
        var canvas=new Canvas {Width=PlotWidth,Height=PlotHeight,ClipToBounds=true};Grid.SetColumn(canvas,1);plot.Children.Add(canvas);
        int gridCount=hasTicks?ticks.Count:HistoryGraph.AxisTickCount;
        for(int i=0;i<gridCount;i++) {
            double y=PlotPadding+i*(PlotHeight-2*PlotPadding)/(gridCount-1);
            canvas.Children.Add(new Line {X1=PlotPadding,X2=PlotWidth-PlotPadding,Y1=y,Y2=y,Stroke=HistoryToolTip.Brush("#344151"),StrokeThickness=1});
        }
        for(int i=0;i<=5;i++) {
            double x=PlotPadding+i*(PlotWidth-2*PlotPadding)/5;
            canvas.Children.Add(new Line {X1=x,X2=x,Y1=PlotHeight-PlotPadding,Y2=PlotHeight-1,Stroke=HistoryToolTip.Brush("#344151"),StrokeThickness=1});
        }
        foreach(var threshold in graph.Thresholds??[]) {
            double y=PlotPadding+(1000-threshold.Y)*(PlotHeight-2*PlotPadding)/1000;
            var line=new Line {X1=PlotPadding,X2=PlotWidth-PlotPadding,Y1=y,Y2=y,Stroke=HistoryToolTip.Brush(threshold.Color),StrokeThickness=1,StrokeDashArray=new([4,3]),Opacity=.8};
            System.Windows.Automation.AutomationProperties.SetName(line,threshold.Label);canvas.Children.Add(line);
        }
        Point ScreenPoint(GraphPoint point)=>new(PlotPadding+point.X*(PlotWidth-2*PlotPadding)/1000,PlotPadding+(1000-point.Y!.Value)*(PlotHeight-2*PlotPadding)/1000);
        Point? latest=graph.Latest is {Y:not null}?ScreenPoint(graph.Latest):null;
        PointCollection segment=new();
        void Flush() {
            if(segment.Count>1) canvas.Children.Add(new Polyline {Points=segment,Stroke=HistoryToolTip.Brush(item.Color),StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round});
            else if(segment.Count==1&&segment[0]!=latest) {
                var dot=new Ellipse {Width=4,Height=4,Fill=HistoryToolTip.Brush(item.Color)};
                Canvas.SetLeft(dot,segment[0].X-2);Canvas.SetTop(dot,segment[0].Y-2);canvas.Children.Add(dot);
            }
            segment=new();
        }
        foreach(var point in graph.Points) {
            if(!point.Y.HasValue) {Flush();continue;}
            segment.Add(ScreenPoint(point));
        }
        Flush();
        if(latest is Point end) {
            var dot=new Ellipse {Width=7,Height=7,Fill=HistoryToolTip.Brush(item.Color),Stroke=HistoryToolTip.Brush("#1A202C"),StrokeThickness=1.5};
            Canvas.SetLeft(dot,end.X-3.5);Canvas.SetTop(dot,end.Y-3.5);canvas.Children.Add(dot);
        }
        Children.Add(plot);
        var time=new Grid {Margin=new Thickness(74,5,0,10)};
        var timeLabels=new Canvas {Width=PlotWidth,Height=16};time.Children.Add(timeLabels);
        for(int i=0;i<=5;i++) {
            var label=Label(i==5?"Now":"−"+(5-i)+"m",10);label.Width=40;
            label.TextAlignment=i==0?TextAlignment.Left:i==5?TextAlignment.Right:TextAlignment.Center;
            double x=PlotPadding+i*(PlotWidth-2*PlotPadding)/5;
            Canvas.SetLeft(label,i==0?PlotPadding:i==5?PlotWidth-PlotPadding-40:x-20);timeLabels.Children.Add(label);
        }
        Children.Add(time);
        Children.Add(Label(graph.Summary,11,"#DCE3ED"));
    }
}
