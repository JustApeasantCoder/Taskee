using System.Runtime.InteropServices;

namespace Taskee.App;
internal sealed class TaskbarSession : IDisposable
{
    private string? session;
    private EventWaitHandle? stop;
    private Process? bridge;
    private uint explorer;
    private bool attaching;
    private long retry;
    public string Status { get; private set; }="Connecting to taskbar";
    public string? SessionPath=>session;
    public async Task Update(PanelSnapshot panel)
    {
        uint pid=GetExplorer();
        if(pid!=explorer) { stop?.Set();stop?.Dispose();stop=null;CloseBridge();session=null;explorer=pid;retry=0; }
        if(session!=null) {
            try {
                ConfigStore.AtomicWrite(Path.Combine(session,"panel.json"),JsonSerializer.Serialize(panel,SensorService.WireJson));
                Status=ReadStatus(panel);
            } catch(Exception ex) { Status="Taskbar connection: "+ex.Message; }
            return;
        }
        if(!panel.Enabled) { Status="Taskbar display is off";return; }
        if(attaching||pid==0||Environment.TickCount64<retry) return;
        attaching=true;
        try {
            string source=Path.Combine(AppContext.BaseDirectory,"TaskeePanel.dll");
            if(!File.Exists(source)) { Status="Build the native taskbar component first";return; }
            // Only the tested Windows 11 taskbar layout is supported in this release.
            using var settings=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if(Convert.ToInt32(settings?.GetValue("TaskbarAl",1))!=0) { Status="Choose left taskbar alignment in Windows for this first release";return; }
            string directory=Path.Combine(AppContext.BaseDirectory,"runtime","sessions",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(directory);File.Copy(source,Path.Combine(directory,"TaskeePanel.dll"));
            File.WriteAllText(Path.Combine(directory,"taskee-owner.txt"),Environment.ProcessId.ToString());
            string eventName=@"Local\Taskee.Panel.Stop."+Guid.NewGuid().ToString("N");
            stop=new EventWaitHandle(false,EventResetMode.ManualReset,eventName);
            File.WriteAllText(Path.Combine(directory,"taskee-event.txt"),eventName);
            ConfigStore.AtomicWrite(Path.Combine(directory,"panel.json"),JsonSerializer.Serialize(panel,SensorService.WireJson));
            int hr=await Attach(pid,Path.Combine(directory,"TaskeePanel.dll"));
            Program.Log($"Taskbar attachment pid={pid} hr=0x{hr:X8} session={directory}");
            if(hr<0) { stop.Set();stop.Dispose();stop=null;Status=$"Taskbar connection failed (0x{hr:X8})";retry=Environment.TickCount64+30000; }
            else { session=directory;Status="Waiting for native taskbar layout"; }
        } catch(Exception ex) { Program.Log(ex.ToString());Status=ex.Message;retry=Environment.TickCount64+30000; }
        finally { attaching=false; }
    }
    private string ReadStatus(PanelSnapshot panel)
    {
        if(!panel.Enabled) return "Taskbar display is off";
        var screens=System.Windows.Forms.Screen.AllScreens.OrderByDescending(s=>s.Primary).ThenBy(s=>s.Bounds.Left).ThenBy(s=>s.Bounds.Top).ToList();
        var windows=new Dictionary<string,IntPtr>();
        EnumWindows((window,_)=> {
            GetWindowThreadProcessId(window,out uint pid);var name=new StringBuilder(64);GetClassName(window,name,name.Capacity);
            if(pid==explorer && name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") windows[System.Windows.Forms.Screen.FromHandle(window).DeviceName]=window;
            return true;
        },IntPtr.Zero);
        var statuses=new List<string>();
        foreach(int number in new[]{1,2,3}.Where(n=>n==1 || (n==2&&panel.SecondMonitor) || (n==3&&panel.ThirdMonitor))) {
            string value;
            if(number>screens.Count) value="Monitor is disconnected";
            else if(!windows.TryGetValue(screens[number-1].DeviceName,out var window)) value="Enable ‘Show my taskbar on all displays’ in Windows";
            else {
                string path=Path.Combine(session!,"taskee-status-"+window.ToInt64()+".txt");
                string initial=Path.Combine(session!,"taskee-status.txt");
                value=File.Exists(path)?File.ReadAllText(path).Trim():File.Exists(initial)?File.ReadAllText(initial).Trim():"Waiting for native taskbar layout";
                if(value.Length==0) value="Waiting for native taskbar layout";
            }
            statuses.Add($"Monitor {number}: {value}");
        }
        string? pending=statuses.FirstOrDefault(s=>!s.Contains(": Connected ·",StringComparison.Ordinal));
        string summary=pending?.Split('\n')[0]??(statuses.Count==1?"Connected · native taskbar space reserved":$"Connected on {statuses.Count} monitors");
        return summary+"\n"+string.Join("\n",statuses);
    }
    private async Task<int> Attach(uint pid,string dll)
    {
        CloseBridge();
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"TaskeeBridge.exe")) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true };
        start.ArgumentList.Add(pid.ToString());start.ArgumentList.Add(dll);start.ArgumentList.Add(Environment.ProcessId.ToString());
        bridge=Process.Start(start)??throw new IOException("Could not start taskbar bridge");
        using var timeout=new CancellationTokenSource(10000);
        string? result=await bridge.StandardOutput.ReadLineAsync(timeout.Token);
        if(result==null||!uint.TryParse(result,System.Globalization.NumberStyles.HexNumber,null,out uint hr)) throw new IOException("Taskbar bridge did not return a result");
        return unchecked((int)hr);
    }
    private void CloseBridge() {try {if(bridge!=null&&!bridge.HasExited) bridge.Kill();}catch {}bridge?.Dispose();bridge=null;}
    public void Dispose() { stop?.Set();stop?.Dispose();stop=null;CloseBridge(); }
    internal static uint GetExplorer() { GetWindowThreadProcessId(FindWindow("Shell_TrayWnd",null),out uint pid);return pid; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string name,string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    private delegate bool WindowCallback(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int maximum);
}
