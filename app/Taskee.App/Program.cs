using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Taskee.App;
internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if(args.Contains("--sensor-helper")) return Helper(args).GetAwaiter().GetResult();
        if(args.Contains("--exit")) { try {using var stop=EventWaitHandle.OpenExisting(@"Local\Taskee.Exit");stop.Set();}catch(WaitHandleCannotBeOpenedException) {}return 0; }
        bool isolatedTest=args.Contains("--ui-test")&&args.Contains("--no-taskbar");
        using var mutex=new Mutex(true,@"Local\Taskee.Options"+(isolatedTest?".QA."+Environment.ProcessId:""),out bool first);
        if(!first) { using var signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\Taskee.ShowOptions");signal.Set();return 0; }
        var application=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException+=(s,e)=>{ Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message,"Taskee",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true; };
        var window=new MainWindow(args);
        application.MainWindow=window;
        if(!args.Contains("--tray")) window.Show();
        return application.Run();
    }
    private static async Task<int> Helper(string[] args)
    {
        string Argument(string key)=>args.SkipWhile(a=>a!=key).Skip(1).FirstOrDefault()??"";
        string pipeName=Argument("--pipe");
        if(!pipeName.StartsWith("Taskee.Sensors.") || !int.TryParse(Argument("--owner"),out int ownerId)) return 2;
        try {
            using var owner=Process.GetProcessById(ownerId);
            using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.Out,PipeOptions.Asynchronous);
            await pipe.ConnectAsync(8000);
            using var collector=new SensorCollector();
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),65536,true) { AutoFlush=true };
            while(!owner.HasExited && pipe.IsConnected) {
                var config=ConfigStore.Load();
                collector.AfterburnerEnabled=config.AfterburnerBridge;
                var frame=collector.Sample();
                frame.SampleIntervalMs=config.RefreshMs;
                await writer.WriteLineAsync(JsonSerializer.Serialize(frame,SensorService.WireJson));
                await Task.Delay(config.RefreshMs);
            }
            return 0;
        } catch(Exception ex) { Log("Sensor helper: "+ex.Message); return 1; }
    }
    internal static void Log(string message) {
        try { Directory.CreateDirectory(ConfigStore.DirectoryPath); string path=Path.Combine(ConfigStore.DirectoryPath,"app.log");if(File.Exists(path)&&new FileInfo(path).Length>2_000_000) File.Move(path,path+".old",true);File.AppendAllText(path,DateTime.Now.ToString("s")+" "+message+Environment.NewLine); } catch { }
    }
}
