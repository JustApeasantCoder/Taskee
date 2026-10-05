using System.IO.Pipes;

namespace Taskee.App;
internal sealed class SensorService : IDisposable
{
    public static readonly JsonSerializerOptions WireJson=new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
    private CancellationTokenSource? cancellation;
    private Process? helper;
    private readonly object gate=new();
    public event Action<SensorFrame>? Frame;
    public event Action<string>? Status;
    public void Start(bool elevated=false)
    {
        Stop();
        var source=new CancellationTokenSource(); cancellation=source;
        _=Task.Run(()=>Run(elevated,source.Token));
    }
    private async Task Run(bool elevated,CancellationToken token)
    {
        bool first=true;
        while(!token.IsCancellationRequested) {
            string name="Taskee.Sensors."+Guid.NewGuid().ToString("N");
            Process? launched=null;
            try {
                using var pipe=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                var start=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=elevated,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--sensor-helper");start.ArgumentList.Add("--pipe");start.ArgumentList.Add(name);start.ArgumentList.Add("--owner");start.ArgumentList.Add(Environment.ProcessId.ToString());
                if(elevated) start.Verb="runas";
                lock(gate) { if(token.IsCancellationRequested) return;launched=helper=Process.Start(start); }
                using var connect=CancellationTokenSource.CreateLinkedTokenSource(token);connect.CancelAfter(15000);
                await pipe.WaitForConnectionAsync(connect.Token);
                using var reader=new StreamReader(pipe,Encoding.UTF8,false,65536,true);
                while(!token.IsCancellationRequested) {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(20000);
                    string? line=await reader.ReadLineAsync(timeout.Token);
                    if(line==null) break;
                    if(line.Length>1_000_000) throw new InvalidDataException("Sensor frame too large");
                    var frame=JsonSerializer.Deserialize<SensorFrame>(line,WireJson);
                    if(frame!=null) Frame?.Invoke(frame);
                }
            } catch(OperationCanceledException) { if(token.IsCancellationRequested) break;Status?.Invoke("Sensor helper timed out; restarting"); }
            catch(Exception ex) { Program.Log("Sensor connection: "+ex.Message); Status?.Invoke("Sensor helper: "+ex.Message);if(elevated&&first) return; }
            finally { KillOwned(launched); }
            // Never repeat a UAC prompt automatically after an elevated helper fails.
            elevated=false;first=false;
            try { await Task.Delay(5000,token); } catch(OperationCanceledException) { break; }
        }
    }
    private void KillOwned(Process? expected=null) { lock(gate) { if(expected!=null&&helper!=expected) return;try { if(helper!=null&&!helper.HasExited) helper.Kill(); } catch { } helper?.Dispose();helper=null; } }
    private void Stop() { cancellation?.Cancel();cancellation?.Dispose();cancellation=null;KillOwned(); }
    public void Dispose()=>Stop();
}
