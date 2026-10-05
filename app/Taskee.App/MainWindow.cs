using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace Taskee.App;
public sealed class MainWindow : Window
{
    private AppConfig config;
    private readonly SettingsSession settings;
    private readonly SettingsAutosave autosave;
    private readonly MetricEngine engine=new();
    private readonly SensorService sensors=new();
    private readonly TaskbarSession taskbar=new();
    private SensorFrame frame=new() { Timestamp=0 };
    private readonly DispatcherTimer timer=new(),saveTimer=new();
    private readonly EventWaitHandle showSignal;
    private readonly EventWaitHandle exitSignal;
    private Forms.NotifyIcon? tray;
    private readonly ContentControl pageHost=new();
    private readonly StackPanel preview=new() { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
    private readonly Border previewBorder=new();
    private readonly Border recoveryBanner=new();
    private readonly TextBlock recoveryText=new();
    private readonly TextBlock connection=new(),sensorStatus=new(),saveStatus=new(),previewNote=new();
    private readonly TextBlock pageTitle=new() {FontSize=25};
    private TextBlock? readingInfo;
    private readonly Dictionary<string,TextBlock> cardValues=[];
    private readonly Dictionary<string,TextBlock> previewValues=[];
    private string previewLayout="";
    private readonly Dictionary<string,Button> navigation=[];
    private ListBox? cards;
    private StackPanel? editor;
    private TextBlock? inventoryStatus;
    private ListView? inventory;
    private string page="Taskbar",deviceSignature="";
    private bool exiting,updating,testing;
    private string? dragId;
    private Point dragStart;
    private readonly Dictionary<string,long> lastNotification=[];
    private readonly string[] arguments;
    private int testStage;
    private readonly List<Forms.NotifyIcon> testIcons=[];
    private readonly long started=Environment.TickCount64;
    private static Brush B(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    public MainWindow(string[] args)
    {
        arguments=args;testing=args.Contains("--ui-test");
        bool temporary=testing||args.Contains("--integration-test")||args.Contains("--label-test");
        string? testPath=testing?args.SkipWhile(a=>a!="--settings-path").Skip(1).FirstOrDefault():null;
        settings=ConfigStore.Open(temporary?testPath??Path.Combine(Path.GetTempPath(),"Taskee-QA-"+Guid.NewGuid().ToString("N"),"settings.json"):null);
        config=settings.Configuration;autosave=new(settings);
        string suffix=testing&&args.Contains("--no-taskbar")?".QA."+Environment.ProcessId:"";
        showSignal=new(false,EventResetMode.AutoReset,@"Local\Taskee.ShowOptions"+suffix);
        exitSignal=new(false,EventResetMode.AutoReset,@"Local\Taskee.Exit"+suffix);
        Title="Taskee · Options";Width=1180;Height=850;MinWidth=980;MinHeight=660;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        using(var icon=TrayIcon.Create()) { Icon=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); }
        Background=B("#10141D");Foreground=B("#ECF0F8");FontFamily=new("Segoe UI");FontSize=13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/Taskee;component/Theme.xaml",UriKind.Relative) });
        Content=BuildShell();
        SourceInitialized+=(s,e)=>{ int dark=1;DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,20,ref dark,4); };
        Closing+=(s,e)=>{ if(!exiting&&config.CloseToTray) { e.Cancel=true;Hide(); } else Exit(); };
        AttachConfiguration();ShowPage("Taskbar");if(!testing) CreateTray();RefreshRecoveryNotice();
        saveTimer.Interval=TimeSpan.FromMilliseconds(450);saveTimer.Tick+=(s,e)=>Save();
        sensors.Frame+=value=>Dispatcher.BeginInvoke(()=>OnFrame(value));
        sensors.Status+=value=>Dispatcher.BeginInvoke(()=>sensorStatus.Text=value);
        if(!testing) { if(!temporary&&settings.IsNew) {autosave.Changed(Environment.TickCount64);saveTimer.Start();}sensors.Start(); }
        timer.Interval=TimeSpan.FromMilliseconds(500);timer.Tick+=async(s,e)=>await Tick();timer.Start();
    }

    private UIElement BuildShell()
    {
        var root=new Grid {Background=B("#10141D")};root.ColumnDefinitions.Add(new() { Width=new GridLength(208) });root.ColumnDefinitions.Add(new());
        var side=new Grid { Background=B("#141A25") };side.RowDefinitions.Add(new());side.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var menu=new StackPanel { Margin=new Thickness(20,28,20,20) };
        var logo=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,35) };
        logo.Children.Add(new Border { Background=B("#6DE0C2"),CornerRadius=new(10),Width=38,Height=38,Child=new TextBlock { Text="T",FontSize=24,FontWeight=FontWeights.Bold,Foreground=B("#10141D"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } });
        logo.Children.Add(new TextBlock { Text="taskee",FontSize=26,FontWeight=FontWeights.SemiBold,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center });menu.Children.Add(logo);
        foreach(var (name,glyph) in new[]{("Taskbar","▤"),("Appearance","◐"),("Sensors","⌁"),("General","⚙")}) {
            var button=Button(glyph+"   "+name,()=>ShowPage(name));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,0,0,8);button.Padding=new Thickness(14,13,14,13);navigation[name]=button;menu.Children.Add(button);
        }
        side.Children.Add(menu);
        var bottom=Text("Taskee "+typeof(MainWindow).Assembly.GetName().Version!.ToString(3),11,"#647289",new Thickness(24,20,20,24));Grid.SetRow(bottom,1);side.Children.Add(bottom);root.Children.Add(side);
        var main=new Grid { Margin=new Thickness(28,26,28,18) };main.RowDefinitions.Add(new() { Height=GridLength.Auto });main.RowDefinitions.Add(new() { Height=GridLength.Auto });main.RowDefinitions.Add(new() { Height=GridLength.Auto });main.RowDefinitions.Add(new());main.RowDefinitions.Add(new() { Height=GridLength.Auto });Grid.SetColumn(main,1);root.Children.Add(main);
        var heading=new DockPanel { Margin=new Thickness(0,0,0,20) };
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
        var pause=Button(config.Paused?"Resume":"Pause",()=>{config.Paused=!config.Paused;});pause.ToolTip="Pause live readings";config.PropertyChanged+=(s,e)=>{ if(e.PropertyName==nameof(config.Paused)) pause.Content=config.Paused?"Resume":"Pause"; };
        buttons.Children.Add(pause);var toggle=Check("On taskbar",config,nameof(config.TaskbarEnabled));toggle.Margin=new Thickness(16,0,0,0);buttons.Children.Add(toggle);DockPanel.SetDock(buttons,Dock.Right);heading.Children.Add(buttons);
        heading.Children.Add(pageTitle);main.Children.Add(heading);
        var recovery=new DockPanel();var recover=Button("Save recovery profile",RecoverSettings,"Preserve the original files and save the current settings");recover.Margin=new Thickness(14,0,0,0);recover.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(recover,Dock.Right);recovery.Children.Add(recover);
        recoveryText.Foreground=B("#F5CF80");recoveryText.FontSize=12;recoveryText.TextWrapping=TextWrapping.Wrap;recovery.Children.Add(recoveryText);
        recoveryBanner.Child=recovery;recoveryBanner.Background=B("#30281C");recoveryBanner.BorderBrush=B("#665132");recoveryBanner.BorderThickness=new(1);recoveryBanner.CornerRadius=new(8);recoveryBanner.Padding=new(14);recoveryBanner.Margin=new(0,0,0,16);Grid.SetRow(recoveryBanner,1);main.Children.Add(recoveryBanner);
        var previewArea=new StackPanel { Margin=new Thickness(0,0,0,20) };
        var label=new DockPanel();previewNote.Text="Preview";previewNote.FontSize=10;previewNote.Foreground=B("#6DE0C2");previewNote.FontWeight=FontWeights.SemiBold;label.Children.Add(previewNote);
        connection.FontSize=11;connection.Foreground=B("#98A4B9");connection.HorizontalAlignment=HorizontalAlignment.Right;label.Children.Add(connection);previewArea.Children.Add(label);
        var bar=new Grid { Height=72,Margin=new Thickness(0,9,0,0) };bar.ColumnDefinitions.Add(new() { Width=new GridLength(110) });bar.ColumnDefinitions.Add(new());
        var weather=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };weather.Children.Add(Text("☀",22,"#FBBF24"));weather.Children.Add(Text("Weather",12,"#DCE3ED",new Thickness(9,0,0,0)));bar.Children.Add(weather);
        previewBorder.Child=preview;previewBorder.VerticalAlignment=VerticalAlignment.Center;previewBorder.HorizontalAlignment=HorizontalAlignment.Left;Grid.SetColumn(previewBorder,1);bar.Children.Add(previewBorder);
        previewArea.Children.Add(new Border { Background=B("#0C1119"),BorderBrush=B("#2C3547"),BorderThickness=new(1),CornerRadius=new(10),Padding=new Thickness(20,0,20,0),Child=bar });Grid.SetRow(previewArea,2);main.Children.Add(previewArea);
        Grid.SetRow(pageHost,3);main.Children.Add(pageHost);
        var footer=new DockPanel { Margin=new Thickness(0,14,0,0) };saveStatus.Foreground=B("#6B7C94");saveStatus.FontSize=11;DockPanel.SetDock(saveStatus,Dock.Right);footer.Children.Add(saveStatus);sensorStatus.Foreground=B("#8291A7");sensorStatus.FontSize=11;sensorStatus.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(sensorStatus);Grid.SetRow(footer,4);main.Children.Add(footer);
        return root;
    }

    private void ShowPage(string name)
    {
        page=name;pageTitle.Text=name;cards=null;editor=null;inventory=null;inventoryStatus=null;readingInfo=null;cardValues.Clear();
        foreach(var pair in navigation) { pair.Value.Background=B(pair.Key==name?"#243B37":"#141A25");pair.Value.BorderBrush=B(pair.Key==name?"#416B60":"#141A25"); }
        pageHost.Content=name switch { "Appearance"=>AppearancePage(),"Sensors"=>SensorsPage(),"General"=>GeneralPage(),_=>TaskbarPage() };
    }
    private UIElement TaskbarPage()
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new() { Width=new GridLength(320) });grid.ColumnDefinitions.Add(new() { Width=new GridLength(20) });grid.ColumnDefinitions.Add(new());
        var left=new Grid();left.RowDefinitions.Add(new() { Height=GridLength.Auto });left.RowDefinitions.Add(new());left.RowDefinitions.Add(new() { Height=GridLength.Auto });
        left.Children.Add(Text("STAT CARDS",10,"#98A4B9",new Thickness(0,0,0,12)));
        cards=new ListBox { AllowDrop=true };Grid.SetRow(cards,1);left.Children.Add(cards);cards.SelectionChanged+=(s,e)=>BuildEditor();
        cards.PreviewMouseLeftButtonDown+=(s,e)=> { var item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);dragId=(item?.Tag as StatConfig)?.Id;dragStart=e.GetPosition(cards); };
        cards.PreviewMouseMove+=(s,e)=> {var position=e.GetPosition(cards);if(e.LeftButton==MouseButtonState.Pressed&&dragId!=null&&(Math.Abs(position.X-dragStart.X)>=SystemParameters.MinimumHorizontalDragDistance||Math.Abs(position.Y-dragStart.Y)>=SystemParameters.MinimumVerticalDragDistance)&&Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem row && row.Tag is StatConfig stat && stat.Id==dragId) { dragId=null;DragDrop.DoDragDrop(row,stat.Id,DragDropEffects.Move); } };
        cards.Drop+=(s,e)=> { if(e.Data.GetData(typeof(string)) is string id&&Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.Tag is StatConfig target) { int from=config.Items.ToList().FindIndex(i=>i.Id==id),to=config.Items.IndexOf(target);if(from>=0&&from!=to) { config.Items.Move(from,to);RefreshCards(id); } } };
        var actions=new StackPanel { Margin=new Thickness(0,4,0,0) };var add=Button("+  Add a stat",()=>{ if(config.Items.Count>=32) { MessageBox.Show("A profile can contain up to 32 cards.","Taskee");return; }var stat=new StatConfig { Metric="cpu.usage",Label="CPU",Color="#A5B4FC" };config.Items.Add(stat);RefreshCards(stat.Id); });add.Background=B("#24413A");add.BorderBrush=B("#406A5C");actions.Children.Add(add);Grid.SetRow(actions,2);left.Children.Add(actions);grid.Children.Add(left);
        editor=new StackPanel();var scroll=new ScrollViewer { Content=editor,Padding=new Thickness(0,0,8,0) };Grid.SetColumn(scroll,2);grid.Children.Add(scroll);
        RefreshCards();return grid;
    }
    private void RefreshCards(string? selected=null)
    {
        if(cards==null) return;
        selected??=(cards.SelectedItem as ListBoxItem)?.Tag is StatConfig old?old.Id:config.Items.FirstOrDefault()?.Id;
        cards.Items.Clear();cardValues.Clear();
        int number=0;
        foreach(var stat in config.Items) {
            var row=new Grid();row.ColumnDefinitions.Add(new() { Width=new GridLength(22) });row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
            row.Children.Add(Text("⠿",18,"#61728A"));var info=new StackPanel();info.Children.Add(Text(Catalog.Name(stat.Metric),13,stat.Enabled?"#ECF0F8":"#697B94"));++number;if(!stat.Enabled||stat.StackWithPrevious) info.Children.Add(Text(!stat.Enabled?"Hidden":"Stacked",10,"#7C8AA1",new Thickness(0,4,0,0)));Grid.SetColumn(info,1);row.Children.Add(info);
            var value=Text("—",14,stat.Color);value.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(value,2);row.Children.Add(value);cardValues[stat.Id]=value;
            var item=new ListBoxItem { Content=row,Tag=stat };System.Windows.Automation.AutomationProperties.SetName(item,Catalog.Name(stat.Metric)+" card "+number);cards.Items.Add(item);if(stat.Id==selected) cards.SelectedItem=item;
        }
        if(cards.SelectedItem==null&&cards.Items.Count>0) cards.SelectedIndex=0;BuildEditor();
    }
    private StatConfig? Selected=>(cards?.SelectedItem as ListBoxItem)?.Tag as StatConfig;
    private void BuildEditor()
    {
        if(editor==null) return;editor.Children.Clear();readingInfo=null;var stat=Selected;if(stat==null) { editor.Children.Add(Text("Add a stat to get started.",18,"#98A4B9"));return; }
        var header=new DockPanel();var controls=new StackPanel { Orientation=Orientation.Horizontal };controls.Children.Add(Button("↑",()=>Move(stat,-1),"Move card up"));var down=Button("↓",()=>Move(stat,1),"Move card down");down.Margin=new Thickness(6,0,0,0);controls.Children.Add(down);var more=Button("Duplicate",()=>{ if(config.Items.Count>=32) {saveStatus.Text="32-card limit reached";return;}var clone=JsonSerializer.Deserialize<StatConfig>(JsonSerializer.Serialize(stat,ConfigStore.Json),ConfigStore.Json)!;clone.Id=Guid.NewGuid().ToString("N");config.Items.Insert(config.Items.IndexOf(stat)+1,clone);RefreshCards(clone.Id); });more.Margin=new Thickness(6,0,0,0);controls.Children.Add(more);DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);header.Children.Add(Text("Edit card",20,"#ECF0F8"));editor.Children.Add(header);
        readingInfo=Text("Waiting for sensors",11,"#8EA59E",new Thickness(0,10,0,0));editor.Children.Add(readingInfo);
        var basic=Section(editor,"Reading");Field(basic,"Metric",Combo(Catalog.Metrics,stat,nameof(stat.Metric)));
        Field(basic,"Label",Box(stat,nameof(stat.Label),24));basic.Children.Add(Check("Show this card",stat,nameof(stat.Enabled)));basic.Children.Add(Check("Stack below the previous card",stat,nameof(stat.StackWithPrevious)));
        if(stat.Metric=="cpu.temp") Field(basic,"Temperature source",Combo(Catalog.CpuSources,stat,nameof(stat.CpuSource)));
        if(stat.Metric.StartsWith("network")) {
            var choices=new List<Choice> { new("auto","Automatic · connected physical adapter") };choices.AddRange(frame.Adapters.Select(a=>new Choice(a.Id,a.Name+(a.Up?"":" (disconnected)"))));Field(basic,"Network adapter",Combo(choices,stat,nameof(stat.Device)));
        } else if(stat.Metric.StartsWith("gpu")||stat.Metric is "cpu.temp" or "cpu.power") {
            var choices=new List<Choice> { new("auto",stat.Metric.StartsWith("gpu")?"Automatic · prefer discrete GPU":"Automatic") };choices.AddRange(frame.Devices.Where(d=>d.Group==(stat.Metric.StartsWith("gpu")?"GPU":"CPU")).Select(d=>new Choice(d.Id,d.Name)));Field(basic,"Device",Combo(choices,stat,nameof(stat.Device)));
        }
        if(stat.Metric=="sensor"||(stat.Metric=="cpu.temp"&&stat.CpuSource=="sensor")) {
            var choices=MetricResolver.SensorChoices(stat,frame).Select(s=>new Choice(s.Id,s.DeviceName+" · "+s.Name+" · "+s.Source)).ToList();
            choices.Insert(0,new("",stat.Metric=="cpu.temp"?"Choose a CPU temperature sensor":"Choose a hardware sensor"));
            if(stat.Sensor.Length>0&&!choices.Any(c=>c.Id==stat.Sensor)) choices.Add(new(stat.Sensor,"Unavailable sensor · choose another"));
            Field(basic,"Sensor",Combo(choices,stat,nameof(stat.Sensor)));
        }
        var aggregation=Section(editor,"Time & units");Field(aggregation,"Display",Combo(Catalog.Readings,stat,nameof(stat.Reading)));
        if(stat.Reading is "average" or "peak" or "minimum") Field(aggregation,"Window · seconds",Number(stat,nameof(stat.WindowSeconds),2,600,true));
        var units=stat.Metric.StartsWith("network")?Catalog.Units.Where(c=>c.Id is "auto" or "bytes" or "binary" or "mbps" or "mib"):stat.Metric.Contains("temp")||(stat.Metric=="sensor"&&frame.Sensors.FirstOrDefault(s=>s.Id==stat.Sensor)?.Unit=="°C")?Catalog.Units.Where(c=>c.Id is "auto" or "c" or "f"):Catalog.Units.Where(c=>c.Id=="auto");
        Field(aggregation,"Units",Combo(units,stat,nameof(stat.Unit)));Field(aggregation,"Decimal places",Combo([new("-1","Automatic"),new("0","0"),new("1","1"),new("2","2")],stat,nameof(stat.Precision),true));
        var appearance=Section(editor,"Color & alerts");Field(appearance,"Card color",ColorField(stat,nameof(stat.Color)));appearance.Children.Add(Check("Use warning and critical colors",stat,nameof(stat.Alerts)));
        if(stat.Alerts) {string unit=MetricResolver.Resolve(stat,frame).Unit;if(unit.Length==0) unit=stat.Metric.Contains("temp")?"°C":stat.Metric.Contains("power")?"W":stat.Metric.StartsWith("network")?"B/s":stat.Metric is "memory.used" or "gpu.memory"?"GiB":stat.Metric is "cpu.usage" or "gpu.usage" or "memory.percent"?"%":"";string suffix=unit.Length>0?" · "+unit:"";Field(appearance,"Warning"+suffix,Number(stat,nameof(stat.Warning),-273,1e9));Field(appearance,"Critical"+suffix,Number(stat,nameof(stat.Critical),-273,1e9)); }
        var remove=Button("Remove card",()=>{ config.Items.Remove(stat);RefreshCards(); });remove.Foreground=B("#F6A6B1");remove.Margin=new Thickness(0,12,0,8);editor.Children.Add(remove);
    }
    private void Move(StatConfig stat,int direction) { int index=config.Items.IndexOf(stat),next=index+direction;if(next<0||next>=config.Items.Count) return;config.Items.Move(index,next);RefreshCards(stat.Id); }

    private UIElement AppearancePage()
    {
        var stack=new StackPanel();
        var presets=new WrapPanel();foreach(string name in new[]{"Balanced","One row","Compact","Readable"}) {var btn=Button(name,()=>ApplyPreset(name));btn.Margin=new Thickness(0,0,8,8);presets.Children.Add(btn);}stack.Children.Add(presets);
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new() { Width=new GridLength(18) });grid.ColumnDefinitions.Add(new());var left=new StackPanel();var right=new StackPanel();Grid.SetColumn(right,2);grid.Children.Add(left);grid.Children.Add(right);stack.Children.Add(grid);
        var type=Section(left,"Typography");var fonts=Fonts.SystemFontFamilies.Select(f=>f.Source).Distinct().OrderBy(f=>f).Select(f=>new Choice(f,f)).ToList();Field(type,"Font family",Combo(fonts,config.Appearance,nameof(AppearanceConfig.Font)));Field(type,"Weight",Combo([new("Normal","Regular"),new("Medium","Medium"),new("SemiBold","Semibold"),new("Bold","Bold")],config.Appearance,nameof(AppearanceConfig.Weight)));Field(type,"Font size · DIP",SliderField(config.Appearance,nameof(AppearanceConfig.FontSize),8,20));type.Children.Add(Check("Show labels",config.Appearance,nameof(AppearanceConfig.ShowLabels)));
        var layout=Section(left,"Layout");Field(layout,"Maximum stacked rows",Combo([new("1","1 · single row"),new("2","2 · paired rows"),new("3","3 · compact rows")],config.Appearance,nameof(AppearanceConfig.MaxRows),true));Field(layout,"Space between columns",SliderField(config.Appearance,nameof(AppearanceConfig.ColumnGap),0,48));Field(layout,"Space between rows",SliderField(config.Appearance,nameof(AppearanceConfig.RowGap),0,8));Field(layout,"Horizontal padding",SliderField(config.Appearance,nameof(AppearanceConfig.Padding),0,24));layout.Children.Add(Check("Show column separators",config.Appearance,nameof(AppearanceConfig.Separators)));layout.Children.Add(Check("Keep widths steady as values change",config.Appearance,nameof(AppearanceConfig.FixedWidths)));
        var color=Section(right,"Panel");Field(color,"Background · #AARRGGBB",ColorField(config.Appearance,nameof(AppearanceConfig.Background)));Field(color,"Opacity",SliderField(config.Appearance,nameof(AppearanceConfig.Opacity),.25,1,.05));Field(color,"Corner radius",SliderField(config.Appearance,nameof(AppearanceConfig.Radius),0,16));color.Children.Add(Check("Show history graphs on hover",config.Appearance,nameof(AppearanceConfig.Tooltips)));
        var width=Section(right,"Width limits");Field(width,"Minimum · DIP (0 = automatic)",Number(config.Appearance,nameof(AppearanceConfig.MinimumWidth),0,600,true));Field(width,"Maximum · DIP",Number(config.Appearance,nameof(AppearanceConfig.MaximumWidth),100,1200,true));
        var alerts=Section(right,"Alert palette");Field(alerts,"Warning",ColorField(config.Appearance,nameof(AppearanceConfig.WarningColor)));Field(alerts,"Critical",ColorField(config.Appearance,nameof(AppearanceConfig.CriticalColor)));
        return new ScrollViewer { Content=stack,Padding=new Thickness(0,0,8,0) };
    }
    private void ApplyPreset(string name)
    {
        var a=config.Appearance;
        a.MaxRows=name=="One row"?1:2;a.FontSize=name=="Readable"?14:name=="Compact"?10:11;a.ColumnGap=name=="Compact"?10:18;a.Padding=name=="Compact"?6:10;a.RowGap=1;a.FixedWidths=true;
        if(name=="Balanced"||name=="Compact"||name=="Readable") for(int i=0;i<config.Items.Count;i++) config.Items[i].StackWithPrevious=i%2==1;
    }
    private UIElement SensorsPage()
    {
        var root=new Grid();root.RowDefinitions.Add(new() { Height=GridLength.Auto });root.RowDefinitions.Add(new());var top=new StackPanel();
        inventoryStatus=Text(frame.Status,12,"#98A4B9",new Thickness(0,7,0,10));inventoryStatus.TextWrapping=TextWrapping.Wrap;inventoryStatus.Visibility=frame.Sensors.Count==0?Visibility.Visible:Visibility.Collapsed;top.Children.Add(inventoryStatus);
        var controls=new WrapPanel();controls.Children.Add(Button("Restart sensor helper",()=>sensors.Start()));var admin=Button("Enable elevated CPU access",()=>{ if(!frame.DriverInstalled) { MessageBox.Show("Direct CPU access requires the PawnIO driver. Your existing Afterburner readings can be used without it. Install PawnIO yourself if you want direct package and core sensors, then use this button.","CPU sensor access",MessageBoxButton.OK,MessageBoxImage.Information);return; }sensors.Start(true); });admin.Margin=new Thickness(8,0,0,0);controls.Children.Add(admin);var website=Button("PawnIO information ↗",()=>Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute=true }));website.Margin=new Thickness(8,0,0,0);controls.Children.Add(website);top.Children.Add(controls);
        top.Children.Add(Check("Read existing MSI Afterburner sensors",config,nameof(config.AfterburnerBridge)));
        top.Margin=new Thickness(0,0,0,14);root.Children.Add(top);
        inventory=new ListView { Background=B("#141A25"),Foreground=B("#ECF0F8"),BorderBrush=B("#2C3547") };
        var view=new GridView();view.Columns.Add(new() { Header="Device",DisplayMemberBinding=new Binding("DeviceName"),Width=200 });view.Columns.Add(new() { Header="Sensor",DisplayMemberBinding=new Binding("Name"),Width=180 });view.Columns.Add(new() { Header="Value",DisplayMemberBinding=new Binding("Display"),Width=100 });view.Columns.Add(new() { Header="Provider",DisplayMemberBinding=new Binding("Source"),Width=150 });inventory.View=view;Grid.SetRow(inventory,1);root.Children.Add(inventory);UpdateInventory();return root;
    }
    private UIElement GeneralPage()
    {
        var stack=new StackPanel();
        var monitors=Section(stack,"Taskbar monitors");monitors.Children.Add(Check("Second monitor",config,nameof(config.SecondMonitor)));monitors.Children.Add(Check("Third monitor",config,nameof(config.ThirdMonitor)));
        var monitoring=Section(stack,"Monitoring");Field(monitoring,"Refresh interval",Combo([new("500","0.5 seconds"),new("1000","1 second"),new("2000","2 seconds"),new("5000","5 seconds")],config,nameof(config.RefreshMs),true));monitoring.Children.Add(Check("Close the options window to the tray",config,nameof(config.CloseToTray)));monitoring.Children.Add(Check("Notify when a card enters its critical range",config,nameof(config.Notifications)));monitoring.Children.Add(Button("Reset all session peaks",()=>{engine.ResetPeaks();saveStatus.Text="Session peaks reset";}));
        var startup=Section(stack,"Startup");var run=new CheckBox { Content="Start Taskee when I sign in",IsChecked=StartupEnabled() };run.Checked+=(s,e)=>SetStartup(true);run.Unchecked+=(s,e)=>SetStartup(false);startup.Children.Add(run);
        var profiles=Section(stack,"Profiles");var buttons=new WrapPanel();foreach(var (label,action) in new (string,Action)[]{("Export profile",Export),("Import profile",Import),("Restore default layout",Defaults),("Open settings folder",()=>Process.Start(new ProcessStartInfo(ConfigStore.DirectoryPath) { UseShellExecute=true }))}) {var button=Button(label,action);button.Margin=new Thickness(0,0,8,8);buttons.Children.Add(button);}profiles.Children.Add(buttons);
        var exit=Button("Exit Taskee",Exit);exit.Margin=new Thickness(0,14,0,12);stack.Children.Add(exit);return new ScrollViewer { Content=stack,Padding=new Thickness(0,0,8,0) };
    }

    private void AttachConfiguration()
    {
        config.PropertyChanged+=Changed;config.Appearance.PropertyChanged+=Changed;foreach(var item in config.Items) item.PropertyChanged+=Changed;
        config.Items.CollectionChanged+=(s,e)=> { if(e.NewItems!=null) foreach(StatConfig item in e.NewItems) item.PropertyChanged+=Changed;if(e.OldItems!=null) foreach(StatConfig item in e.OldItems) item.PropertyChanged-=Changed;Changed(config,new("Items")); };
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)
    {
        autosave.Changed(Environment.TickCount64);saveStatus.Text=autosave.Status;saveTimer.Stop();if(settings.CanSave) saveTimer.Start();
        if(sender is StatConfig stat) {
            if(e.PropertyName==nameof(StatConfig.Metric)) { stat.Device="auto";stat.Sensor="";stat.Unit="auto";stat.Label=Catalog.Label(stat.Metric); }
            if(e.PropertyName==nameof(StatConfig.Sensor)) stat.Unit="auto";
            if(e.PropertyName is nameof(StatConfig.Metric) or nameof(StatConfig.Sensor) or nameof(StatConfig.Device) or nameof(StatConfig.CpuSource) or nameof(StatConfig.Reading) or nameof(StatConfig.Alerts)) Dispatcher.BeginInvoke(()=>{ if(Selected==stat) BuildEditor(); });
            if(e.PropertyName is nameof(StatConfig.Enabled) or nameof(StatConfig.StackWithPrevious) or nameof(StatConfig.Metric)) Dispatcher.BeginInvoke(()=>RefreshCards(stat.Id));
        }
    }
    private void Save(bool force=false)
    {
        if(!autosave.Pending) {saveTimer.Stop();return;}
        if(testing||arguments.Contains("--integration-test")||arguments.Contains("--label-test")) autosave.Saved();
        else {
            var previous=autosave.LastError;autosave.TrySave(config,Environment.TickCount64,force);
            if(autosave.LastError!=null&&autosave.LastError!=previous) Program.Log("Settings save: "+autosave.LastError.Message);
        }
        saveStatus.Text=autosave.Pending||!settings.CanSave?autosave.Status:"";
        if(!autosave.Pending||!settings.CanSave||exiting) saveTimer.Stop();
    }
    private void RefreshRecoveryNotice() {recoveryBanner.Visibility=settings.CanSave?Visibility.Collapsed:Visibility.Visible;recoveryText.Text=settings.Notice;if(!settings.CanSave) saveStatus.Text="Profile protected · changes kept in memory";}
    private void RecoverSettings()
    {
        try {string path=settings.Recover(config);autosave.Saved();saveTimer.Stop();RefreshRecoveryNotice();saveStatus.Text="Recovery profile saved · originals preserved";saveStatus.ToolTip=path;}
        catch(Exception ex) {saveStatus.Text="Recovery failed: "+ex.Message;Program.Log("Settings recovery: "+ex.Message);}
    }
    private void OnFrame(SensorFrame value)
    {
        frame=value;sensorStatus.Text="";
        string signature=string.Join('|',value.Devices.Select(d=>d.Id))+string.Join('|',value.Adapters.Select(a=>a.Id))+string.Join('|',value.Sensors.Select(s=>s.Id));
        if(signature!=deviceSignature) { deviceSignature=signature;if(page=="Taskbar") BuildEditor(); }
        if(page=="Sensors") UpdateInventory();
    }
    private async Task Tick()
    {
        if(exiting||updating) return;updating=true;
        try {
            if(showSignal.WaitOne(0)) ShowOptions();
            if(exitSignal.WaitOne(0)) {Exit();return;}
            var panel=engine.Build(config,frame,Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);
            UpdatePreview(panel);
            foreach(var column in panel.Columns) foreach(var item in column.Items) if(cardValues.TryGetValue(item.Id,out var label)) {label.Text=item.Value;label.Foreground=B(item.Color);HistoryToolTip.Set(label,item,config.Appearance.Tooltips);}
            if(readingInfo!=null&&Selected is StatConfig selected&&engine.Last.TryGetValue(selected.Id,out var reading)) {readingInfo.Text=reading.Value.HasValue?"":reading.Status;readingInfo.Visibility=reading.Value.HasValue?Visibility.Collapsed:Visibility.Visible;}
            if(!arguments.Contains("--no-taskbar")) await taskbar.Update(panel);
            string taskbarStatus=taskbar.Status.Split('\n')[0];connection.Text=taskbarStatus.StartsWith("Connected ·",StringComparison.Ordinal)?"Connected":taskbarStatus;connection.ToolTip=null;CheckNotifications(panel);
            if(testing) UiTest();
            if(arguments.Contains("--integration-test")) IntegrationTest();
            if(arguments.Contains("--label-test")) LabelTest();
        } catch(Exception ex) { Program.Log(ex.ToString());connection.Text=ex.Message; }
        finally { updating=false; }
    }
    private void UpdatePreview(PanelSnapshot panel)
    {
        var a=panel.Appearance;
        previewBorder.Background=B(a.Background);previewBorder.CornerRadius=new(a.Radius);previewBorder.Padding=new Thickness(a.Padding,2,a.Padding,2);previewBorder.Opacity=a.Opacity;
        if(previewLayout==panel.LayoutKey) {
            foreach(var item in panel.Columns.SelectMany(c=>c.Items)) if(previewValues.TryGetValue(item.Id,out var text)) {
                text.Text=(item.Label.Length>0?item.Label+"  ":"")+item.Value;text.Foreground=B(item.Color);HistoryToolTip.Set(text,item,a.Tooltips);
            }
            foreach(var row in preview.Children.OfType<StackPanel>()) {
                double widest=0;
                foreach(TextBlock text in row.Children) {text.Measure(new Size(10000,100));widest=Math.Max(widest,text.DesiredSize.Width);}
                row.Width=a.FixedWidths?Math.Max(row.Width,Math.Ceiling(widest)):Math.Ceiling(widest);
            }
            previewNote.Text=panel.Paused?"Paused":"Preview";return;
        }
        foreach(var text in previewValues.Values) if(text.ToolTip is ToolTip tip) tip.IsOpen=false;
        preview.Children.Clear();previewValues.Clear();previewLayout=panel.LayoutKey;
        foreach(var column in panel.Columns) {
            if(preview.Children.Count>0&&a.Separators) preview.Children.Add(new Border { Background=B("#344151"),Width=1,Margin=new Thickness(a.ColumnGap/2,4,a.ColumnGap/2,4) });
            var rows=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(preview.Children.Count==0||a.Separators?0:a.ColumnGap,0,0,0) };
            double widest=0;
            foreach(var item in column.Items) {
                var text=new TextBlock { Text=(item.Label.Length>0?item.Label+"  ":"")+item.Value,FontSize=Math.Min(a.FontSize,(42-(column.Items.Count-1)*a.RowGap)/column.Items.Count/1.35),FontFamily=new(a.Font),FontWeight=(FontWeight)new FontWeightConverter().ConvertFromString(a.Weight)!,Foreground=B(item.Color),Margin=new Thickness(0,0,0,a.RowGap) };
                previewValues[item.Id]=text;HistoryToolTip.Set(text,item,a.Tooltips);
                text.Measure(new Size(10000,100));widest=Math.Max(widest,text.DesiredSize.Width);
                if(a.FixedWidths) {string actual=text.Text;text.Text=(item.Label.Length>0?item.Label+"  ":"")+item.WidthHint;text.Measure(new Size(10000,100));widest=Math.Max(widest,text.DesiredSize.Width);text.Text=actual;}
                rows.Children.Add(text);
            }
            rows.Width=Math.Ceiling(widest);
            preview.Children.Add(rows);
        }
        previewBorder.MaxWidth=a.MaximumWidth;previewBorder.MinWidth=a.MinimumWidth;previewBorder.ClipToBounds=true;
        previewNote.Text=panel.Paused?"Paused":"Preview";
    }
    private void UpdateInventory()
    {
        if(inventory==null) return;
        inventory.ItemsSource=frame.Sensors.Where(s=>s.Unit.Length>0).Select(s=>new {s.DeviceName,s.Name,s.Source,Display=s.Value.HasValue?s.Value.Value.ToString("0.##")+" "+s.Unit:"—"}).ToList();
        if(inventoryStatus!=null) {inventoryStatus.Text=frame.Status;inventoryStatus.Visibility=frame.Sensors.Count==0?Visibility.Visible:Visibility.Collapsed;}
    }
    private void CheckNotifications(PanelSnapshot panel)
    {
        if(!config.Notifications||tray==null||config.Paused) return;
        foreach(var stat in config.Items.Where(s=>s.Enabled&&s.Alerts)) {
            var value=panel.Columns.SelectMany(c=>c.Items).FirstOrDefault(i=>i.Id==stat.Id);
            if(value?.Number>=stat.Critical && (!lastNotification.TryGetValue(stat.Id,out long last)||Environment.TickCount64-last>300000)) {lastNotification[stat.Id]=Environment.TickCount64;tray.ShowBalloonTip(4000,Catalog.Name(stat.Metric),value.Value+" · critical threshold reached",Forms.ToolTipIcon.Warning);}
        }
    }
    private void CreateTray()
    {
        tray=new Forms.NotifyIcon { Text="Taskee · taskbar stats",Icon=TrayIcon.Create(),Visible=true };
        tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(ShowOptions);
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("Options",null,(s,e)=>Dispatcher.Invoke(ShowOptions));menu.Items.Add("Show / hide taskbar stats",null,(s,e)=>Dispatcher.Invoke(()=>config.TaskbarEnabled=!config.TaskbarEnabled));menu.Items.Add("Pause / resume",null,(s,e)=>Dispatcher.Invoke(()=>config.Paused=!config.Paused));menu.Items.Add("Reset session peaks",null,(s,e)=>Dispatcher.Invoke(engine.ResetPeaks));menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Exit",null,(s,e)=>Dispatcher.Invoke(Exit));tray.ContextMenuStrip=menu;
    }
    private void ShowOptions() { Show();WindowState=WindowState.Normal;Activate(); }
    private void Export() { var dialog=new SaveFileDialog { Title="Export Taskee profile",Filter="Taskee profile (*.json)|*.json",FileName="Taskee-profile.json" };if(dialog.ShowDialog(this)==true) try {ConfigStore.Validate(config);File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(config,ConfigStore.Json));saveStatus.Text="Profile exported";}catch(Exception ex) {MessageBox.Show(ex.Message,"Export profile");} }
    private void Import() { var dialog=new OpenFileDialog { Title="Import Taskee profile",Filter="Taskee profile (*.json)|*.json" };if(dialog.ShowDialog(this)!=true) return;try {if(new FileInfo(dialog.FileName).Length>1_000_000) throw new InvalidDataException("Profile is too large.");var imported=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(dialog.FileName),ConfigStore.Json)??throw new InvalidDataException("Empty profile");ConfigStore.Validate(imported);ReplaceConfiguration(imported);}catch(Exception ex) {MessageBox.Show(ex.Message,"Import profile",MessageBoxButton.OK,MessageBoxImage.Warning);} }
    private void Defaults() { if(MessageBox.Show("Replace your cards and appearance with the default layout?","Restore defaults",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes) ReplaceConfiguration(AppConfig.Default()); }
    private void ReplaceConfiguration(AppConfig imported) { config.Items.Clear();foreach(var item in imported.Items) config.Items.Add(item);foreach(var property in typeof(AppearanceConfig).GetProperties().Where(p=>p.CanWrite)) property.SetValue(config.Appearance,property.GetValue(imported.Appearance));config.RefreshMs=imported.RefreshMs;config.AfterburnerBridge=imported.AfterburnerBridge;config.Notifications=imported.Notifications;config.TaskbarEnabled=imported.TaskbarEnabled;config.SecondMonitor=imported.SecondMonitor;config.ThirdMonitor=imported.ThirdMonitor;config.CloseToTray=imported.CloseToTray;config.Paused=imported.Paused;engine.Reset();ShowPage(page); }
    private static bool StartupEnabled() {using var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");return run?.GetValue("Taskee")!=null;}
    private void SetStartup(bool enable) {try {using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(enable) run.SetValue("Taskee","\""+Environment.ProcessPath+"\" --tray");else run.DeleteValue("Taskee",false);}catch(Exception ex) {MessageBox.Show(ex.Message,"Startup");}}
    private void Exit()=>Exit(0);
    private void Exit(int exitCode) {if(exiting) return;exiting=true;timer.Stop();Save(true);taskbar.Dispose();sensors.Dispose();showSignal.Dispose();exitSignal.Dispose();if(tray!=null) {tray.Visible=false;tray.Icon?.Dispose();tray.Dispose();}Application.Current.Shutdown(exitCode);}

    private static TextBlock Text(string text,double size=13,string color="#ECF0F8",Thickness? margin=null)=>new() { Text=text,FontSize=size,Foreground=B(color),Margin=margin??new(),TextWrapping=TextWrapping.Wrap };
    private static Button Button(string label,Action action,string? tooltip=null) {var b=new Button { Content=label,ToolTip=tooltip };b.Click+=(s,e)=>action();System.Windows.Automation.AutomationProperties.SetName(b,tooltip??label);return b;}
    private static StackPanel Section(Panel parent,string title) {var content=new StackPanel();content.Children.Add(Text(title,14,"#DCE3ED",new Thickness(0,0,0,10)));parent.Children.Add(new Border {Background=B("#1A202C"),BorderBrush=B("#2C3547"),BorderThickness=new(1),CornerRadius=new(10),Padding=new Thickness(18),Margin=new Thickness(0,14,0,0),Child=content});return content;}
    private static void Field(Panel parent,string label,UIElement control) {parent.Children.Add(Text(label,11,"#98A4B9",new Thickness(0,5,0,6)));parent.Children.Add(control);if(control is FrameworkElement element) element.Margin=new Thickness(0,0,0,9);if(control is DependencyObject obj) System.Windows.Automation.AutomationProperties.SetName(obj,label);}
    private static CheckBox Check(string label,object source,string path) {var c=new CheckBox {Content=label};c.SetBinding(CheckBox.IsCheckedProperty,new Binding(path) {Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});return c;}
    private static TextBox Box(object source,string path,int max=0) {var box=new TextBox {MaxLength=max};box.SetBinding(TextBox.TextProperty,new Binding(path) {Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});return box;}
    private static ComboBox Combo(IEnumerable<Choice> choices,object source,string path,bool numeric=false) {var combo=new ComboBox {ItemsSource=choices.ToList(),DisplayMemberPath="Name",SelectedValuePath="Id"};combo.SetBinding(ComboBox.SelectedValueProperty,new Binding(path) {Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,Converter=numeric?new IntegerStringConverter():null});return combo;}
    private static UIElement Number(object source,string path,double min,double max,bool integer=false) {var box=new TextBox {MaxLength=14};box.SetBinding(TextBox.TextProperty,new Binding(path) {Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true,Converter=new BoundedNumberConverter(min,max,integer)});box.ToolTip=$"{min} to {max}";return box;}
    private static UIElement SliderField(object source,string path,double min,double max,double step=1) {var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new() {Width=new GridLength(52)});var slider=new Slider {Minimum=min,Maximum=max,TickFrequency=step,IsSnapToTickEnabled=true,VerticalAlignment=VerticalAlignment.Center};slider.SetBinding(Slider.ValueProperty,new Binding(path) {Source=source,Mode=BindingMode.TwoWay});grid.Children.Add(slider);var label=Text("",12,"#DCE3ED");label.HorizontalAlignment=HorizontalAlignment.Right;label.SetBinding(TextBlock.TextProperty,new Binding(path) {Source=source,StringFormat="0.##"});Grid.SetColumn(label,1);grid.Children.Add(label);return grid;}
    private static UIElement ColorField(object source,string path) {var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new() {Width=new GridLength(43)});var box=new TextBox {MaxLength=9};box.SetBinding(TextBox.TextProperty,new Binding(path) {Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidationRules={new HexColorRule()}});grid.Children.Add(box);var picker=Button("●",()=>{using var dialog=new Forms.ColorDialog {FullOpen=true};string hex=(string)source.GetType().GetProperty(path)!.GetValue(source)!;try {dialog.Color=System.Drawing.ColorTranslator.FromHtml(hex.Length==9?"#"+hex[3..]:hex);}catch {}if(dialog.ShowDialog()==Forms.DialogResult.OK) source.GetType().GetProperty(path)!.SetValue(source,"#"+dialog.Color.R.ToString("X2")+dialog.Color.G.ToString("X2")+dialog.Color.B.ToString("X2"));},"Choose color");picker.Margin=new Thickness(7,0,0,0);picker.Padding=new Thickness(6);picker.SetBinding(System.Windows.Controls.Button.ForegroundProperty,new Binding(path) {Source=source,Converter=new HexBrushConverter()});Grid.SetColumn(picker,1);grid.Children.Add(picker);return grid;}
    private static T? Ancestor<T>(DependencyObject? node) where T:DependencyObject {while(node!=null) {if(node is T found) return found;node=VisualTreeHelper.GetParent(node);}return null;}
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);

    private void UiTest()
    {
        if(arguments.Contains("--history-test")) {
            string directory=arguments.SkipWhile(a=>a!="--capture-dir").Skip(1).FirstOrDefault()??Path.Combine(Path.GetTempPath(),"Taskee-history-QA");
            int exitCode=0;
            try {HistoryUiChecks.Run(UpdatePreview,id=>previewValues[id],directory);}
            catch(Exception ex) {exitCode=1;Program.Log("History UI checks failed: "+ex);try {Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"history-ui-checks.json"),JsonSerializer.Serialize(new {Passed=false,Error=ex.ToString()}));}catch(Exception reportError) {Program.Log("History UI report failed: "+reportError.Message);}}
            finally {Exit(exitCode);}
            return;
        }
        long elapsed=Environment.TickCount64-started;
        if(elapsed>1000&&testStage==0) {Capture("taskbar");ShowPage("Appearance");testStage++;}
        else if(elapsed>4000&&testStage==1) {Capture("appearance");ShowPage("Sensors");testStage++;}
        else if(elapsed>7000&&testStage==2) {Capture("sensors");ShowPage("General");testStage++;}
        else if(elapsed>10000&&testStage==3) {Capture("general");ShowPage("Taskbar");testStage++;}
        else if(elapsed>13000&&testStage==4) {Program.Log("UI test: all four pages created without exceptions");Exit();}
    }
    private void IntegrationTest()
    {
        long elapsed=Environment.TickCount64-started;
        if(elapsed>6000&&testStage==0) {Program.Log("Integration default: "+taskbar.Status);Capture("live-taskbar");ApplyPreset("One row");testStage++;}
        else if(elapsed>10000&&testStage==1) {Program.Log("Integration one row: "+taskbar.Status);config.Items.Move(4,0);config.Appearance.MaxRows=3;config.Appearance.FontSize=16;config.Appearance.Background="#4020303A";config.Appearance.Separators=true;testStage++;}
        else if(elapsed>15000&&testStage==2) {Program.Log("Integration reordered: "+taskbar.Status);config.TaskbarEnabled=false;testStage++;}
        else if(elapsed>18000&&testStage==3) {Program.Log("Integration display off: "+taskbar.Status);config.TaskbarEnabled=true;ApplyPreset("Balanced");testStage++;}
        else if(elapsed>23000&&testStage==4) {Program.Log("Integration enabled again: "+taskbar.Status);ShowPage("Appearance");testStage++;}
        else if(elapsed>25000&&testStage==5) {Capture("live-appearance");ShowPage("Sensors");testStage++;}
        else if(elapsed>27000&&testStage==6) {Capture("live-sensors");ShowPage("General");testStage++;}
        else if(elapsed>29000&&testStage==7) {Capture("live-general");Program.Log("Integration test complete");Exit();}
    }
    private void Capture(string name)
    {
        string? path=arguments.SkipWhile(a=>a!="--capture-dir").Skip(1).FirstOrDefault();if(path==null) return;
        Directory.CreateDirectory(path);UpdateLayout();var visual=(FrameworkElement)Content;
        if(visual.ActualWidth<=0||visual.ActualHeight<=0) {visual.Measure(new Size(Width-16,Height-40));visual.Arrange(new Rect(0,0,Width-16,Height-40));visual.UpdateLayout();}
        var dpi=VisualTreeHelper.GetDpi(visual);
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(visual.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bitmap.Render(visual);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(path,name+".png"));png.Save(file);
    }
    private void LabelTest()
    {
        long elapsed=Environment.TickCount64-started;
        if(elapsed>7000&&testStage==0) {Program.Log("Label test initial: "+taskbar.Status);config.Items[4].Label="↓ TEST";for(int i=0;i<4;i++) testIcons.Add(new Forms.NotifyIcon {Icon=TrayIcon.Create(),Text="Taskee temporary tray test "+i,Visible=true});testStage++;}
        else if(elapsed>13000&&testStage==1) {Program.Log("Label test wider: "+taskbar.Status);config.Items[4].Label="↓";foreach(var icon in testIcons) {icon.Visible=false;icon.Icon?.Dispose();icon.Dispose();}testIcons.Clear();testStage++;}
        else if(elapsed>19000&&testStage==2) {Program.Log("Label test restored: "+taskbar.Status);Exit();}
    }
}

internal sealed class IntegerStringConverter : IValueConverter
{
    public object Convert(object value,Type target,object parameter,System.Globalization.CultureInfo culture)=>value.ToString()!;
    public object ConvertBack(object value,Type target,object parameter,System.Globalization.CultureInfo culture)=>int.TryParse(value?.ToString(),out int parsed)?parsed:Binding.DoNothing;
}
internal sealed class BoundedNumberConverter(double min,double max,bool integer) : IValueConverter
{
    public object Convert(object value,Type target,object parameter,System.Globalization.CultureInfo culture)=>System.Convert.ToDouble(value).ToString("0.##",culture);
    public object ConvertBack(object value,Type target,object parameter,System.Globalization.CultureInfo culture) {if(!double.TryParse(value?.ToString(),culture,out double number)||!double.IsFinite(number)||number<min||number>max) throw new ArgumentException($"Enter a value from {min} to {max}.");return integer?(object)(int)Math.Round(number):number;}
}
internal sealed class HexColorRule : ValidationRule {public override ValidationResult Validate(object value,System.Globalization.CultureInfo culture)=>System.Text.RegularExpressions.Regex.IsMatch(value?.ToString()??"","^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")?ValidationResult.ValidResult:new(false,"Use #RRGGBB or #AARRGGBB.");}
internal sealed class HexBrushConverter : IValueConverter {public object Convert(object value,Type target,object parameter,System.Globalization.CultureInfo culture) {try{return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value.ToString()!));}catch{return Brushes.White;}}public object ConvertBack(object value,Type target,object parameter,System.Globalization.CultureInfo culture)=>Binding.DoNothing;}
internal static class TrayIcon
{
    public static System.Drawing.Icon Create() {
        using var bitmap=new System.Drawing.Bitmap(32,32);using var graphics=System.Drawing.Graphics.FromImage(bitmap);graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;graphics.Clear(System.Drawing.Color.Transparent);using var background=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(20,26,37));graphics.FillEllipse(background,1,1,30,30);using var pen=new System.Drawing.Pen(System.Drawing.Color.FromArgb(109,224,194),3) {StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};graphics.DrawLine(pen,8,21,8,15);graphics.DrawLine(pen,16,21,16,10);graphics.DrawLine(pen,24,21,24,13);IntPtr handle=bitmap.GetHicon();using var icon=System.Drawing.Icon.FromHandle(handle);var clone=(System.Drawing.Icon)icon.Clone();DestroyIcon(handle);return clone;
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
