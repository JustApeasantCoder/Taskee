using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;

namespace Taskee.Core;

public sealed class SensorCollector : IDisposable
{
    private readonly Computer computer = new();
    private readonly Dictionary<string,(long Received,long Sent,long Time)> network = [];
    private readonly HashSet<int> physicalIndexes = [];
    private readonly string cpuName;
    private readonly bool driverInstalled, elevated;
    private ulong previousIdle, previousKernel, previousUser;
    private bool cpuBaseline;
    private long lastAdapterScan;
    private List<NetworkInterface> adapters = [];
    private string hardwareError = "";
    public bool AfterburnerEnabled { get; set; } = true;

    public SensorCollector()
    {
        using var identity = WindowsIdentity.GetCurrent();
        elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        driverInstalled = service != null;
        using var processor = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        cpuName = processor?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "CPU";
        computer.IsCpuEnabled = driverInstalled && elevated;
        computer.IsGpuEnabled = true;
        try { computer.Open(); } catch(Exception ex) { hardwareError = ex.Message; }
        ScanAdapters();
    }
    private void ScanAdapters()
    {
        lastAdapterScan = Environment.TickCount64;
        try { adapters = NetworkInterface.GetAllNetworkInterfaces().ToList(); } catch { adapters = []; }
        try {
            using var query = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT InterfaceIndex FROM MSFT_NetAdapter WHERE HardwareInterface = TRUE");
            using var results = query.Get();
            physicalIndexes.Clear();
            foreach(ManagementObject result in results) { using(result) { if(result["InterfaceIndex"] is uint index) physicalIndexes.Add((int)index); } }
        } catch { }
    }
    public SensorFrame Sample()
    {
        long started=Stopwatch.GetTimestamp();
        var frame = new SensorFrame { DriverInstalled = driverInstalled, Elevated = elevated };
        frame.Devices.Add(new("cpu", cpuName, "CPU"));
        foreach(var hardware in computer.Hardware) ReadHardware(hardware, frame);
        ReadSystem(frame); ReadNetwork(frame);
        string bridgeStatus = AfterburnerEnabled ? Afterburner.Read(frame, cpuName) : "Afterburner bridge disabled";
        bool cpuLive = frame.Sensors.Any(s => s.Group == "CPU" && s.Type == "Temperature" && s.Value.HasValue);
        string access = cpuLive ? "CPU sensors live" : !driverInstalled ? "CPU sensors need PawnIO, or a running Afterburner monitor" : !elevated ? "CPU sensor helper needs administrator access" : "CPU sensors are unavailable";
        frame.Status = access + " · " + bridgeStatus + (hardwareError.Length > 0 ? " · " + hardwareError : "");
        frame.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        frame.CollectionDurationMs=(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return frame;
    }
    private void ReadHardware(IHardware hardware, SensorFrame frame)
    {
        try { hardware.Update(); } catch { return; }
        string group = hardware.HardwareType == HardwareType.Cpu ? "CPU" : hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel ? "GPU" : "Other";
        string id = group == "CPU" ? "cpu" : hardware.Identifier.ToString();
        if(group != "Other" && !frame.Devices.Any(d => d.Id == id)) frame.Devices.Add(new(id, hardware.Name, group));
        foreach(var sensor in hardware.Sensors) {
            string type = sensor.SensorType.ToString();
            string unit = sensor.SensorType switch { SensorType.Temperature => "°C", SensorType.Power => "W", SensorType.Load => "%", SensorType.SmallData => "MB", SensorType.Data => "GB", SensorType.Clock => "MHz", SensorType.Fan => "RPM", SensorType.Voltage => "V", _ => "" };
            double? value = sensor.Value.HasValue && float.IsFinite(sensor.Value.Value) ? sensor.Value.Value : null;
            frame.Sensors.Add(new("lhm:" + sensor.Identifier, id, hardware.Name, group, sensor.Name, type, SensorKinds.Classify(group,type,sensor.Name), unit, value, "Libre Hardware Monitor"));
        }
        foreach(var child in hardware.SubHardware) ReadHardware(child,frame);
    }
    private void ReadSystem(SensorFrame frame)
    {
        if(GetSystemTimes(out ulong idle,out ulong kernel,out ulong user)) {
            if(cpuBaseline && kernel >= previousKernel && user >= previousUser && idle >= previousIdle) {
                ulong total = kernel - previousKernel + user - previousUser, inactive = idle - previousIdle;
                frame.System["cpu.usage"] = total > 0 ? Math.Clamp(100.0 * (total - Math.Min(total,inactive)) / total,0,100) : null;
            }
            previousIdle = idle; previousKernel = kernel; previousUser = user; cpuBaseline = true;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if(GlobalMemoryStatusEx(ref memory)) {
            frame.System["memory.percent"] = memory.TotalPhysical > 0 ? 100.0 * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical : null;
            frame.System["memory.used"] = (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0;
        }
    }
    private void ReadNetwork(SensorFrame frame)
    {
        if(Environment.TickCount64 - lastAdapterScan > 30000) ScanAdapters();
        foreach(var adapter in adapters) {
            if(adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try {
                int index = adapter.GetIPProperties().GetIPv4Properties()?.Index ?? -1;
                bool isPhysical = physicalIndexes.Contains(index);
                bool up = adapter.OperationalStatus == OperationalStatus.Up;
                frame.Adapters.Add(new(adapter.Id, adapter.Name + " · " + adapter.Description, isPhysical, up));
                var counters = adapter.GetIPStatistics();
                long now = Stopwatch.GetTimestamp();
                if(network.TryGetValue(adapter.Id,out var before) && up) {
                    double elapsed = (now - before.Time) / (double)Stopwatch.Frequency;
                    var rates = CalculateRates(before.Received,before.Sent,counters.BytesReceived,counters.BytesSent,elapsed);
                    frame.System["network.down:" + adapter.Id] = rates.Down;
                    frame.System["network.up:" + adapter.Id] = rates.Up;
                }
                network[adapter.Id] = (counters.BytesReceived,counters.BytesSent,now);
            } catch { }
        }
    }
    public static (double? Down,double? Up) CalculateRates(long oldReceived,long oldSent,long received,long sent,double seconds)
        => seconds <= 0 || !double.IsFinite(seconds) || received < oldReceived || sent < oldSent ? (null,null) : ((received-oldReceived)/seconds,(sent-oldSent)/seconds);
    public void Dispose() { try { computer.Close(); } catch { } }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended; }
}

// Documented MSI Afterburner monitoring shared memory. Read-only; no SDK code is redistributed.
internal static class Afterburner
{
    public static string Read(SensorFrame frame,string cpuName)
    {
        try {
            using var map = MemoryMappedFile.OpenExisting("MAHMSharedMemory",MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
            uint signature = view.ReadUInt32(0), header = view.ReadUInt32(8), count = view.ReadUInt32(12), entry = view.ReadUInt32(16);
            uint gpuCount = view.ReadUInt32(24), gpuSize = view.ReadUInt32(28);
            if(signature != 0x4D41484D || header < 32 || entry < 1324 || count > 4096 || gpuCount > 32 || gpuSize < 1304 || (long)header + (long)count*entry + (long)gpuCount*gpuSize > view.Capacity) return "Afterburner format is unsupported";
            long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - view.ReadUInt32(20);
            if(age > 10 || age < -5) return "Afterburner readings are stale";
            var gpuDevices = new Dictionary<uint,DeviceInfo>();
            for(uint i=0; i<gpuCount; i++) {
                string name = ReadText(view, header + (long)count*entry + (long)i*gpuSize + 520);
                if(name.Length == 0) continue;
                var found = frame.Devices.FirstOrDefault(d => d.Group == "GPU" && (d.Name.Contains(name,StringComparison.OrdinalIgnoreCase) || name.Contains(d.Name,StringComparison.OrdinalIgnoreCase)));
                found ??= new DeviceInfo("afterburner:gpu:"+i,name,"GPU");
                if(!frame.Devices.Any(d=>d.Id==found.Id)) frame.Devices.Add(found);
                gpuDevices[i] = found;
            }
            int added = 0;
            for(uint i=0; i<count; i++) {
                long position = header + (long)i*entry;
                uint source = view.ReadUInt32(position+1320), gpuIndex = view.ReadUInt32(position+1316);
                string name = ReadText(view,position), units = ReadText(view,position+260);
                string group = source is 0x80 or 0x90 or 0xA0 or 0x100 ? "CPU" : source is 0 or 0x30 or 0x31 or 0x61 ? "GPU" : "Other";
                if(group == "Other") continue; // Relative power (0x60) must never be read as watts.
                string type = source switch { 0 or 0x80 => "Temperature", 0x61 or 0x100 => "Power", 0x30 or 0x90 => "Load", 0x31 => "SmallData", _ => "Clock" };
                string unit = type switch { "Temperature" => "°C", "Power" => "W", "Load" => "%", "SmallData" => "MB", _ => units };
                double raw = view.ReadSingle(position+1300);
                double? value = double.IsFinite(raw) && Math.Abs(raw)<1e9 ? raw : null;
                var device = group == "CPU" ? new DeviceInfo("cpu",cpuName,"CPU") : gpuDevices.GetValueOrDefault(gpuIndex);
                if(device == null) continue;
                frame.Sensors.Add(new("afterburner:"+gpuIndex+":"+source+":"+name,device.Id,device.Name,group,name,type,SensorKinds.Classify(group,type,name),unit,value,"MSI Afterburner")); added++;
            }
            return added > 0 ? "Afterburner bridge live" : "Afterburner has no matching sensors";
        } catch(FileNotFoundException) { return "Afterburner is not sharing readings"; }
        catch(UnauthorizedAccessException) { return "Afterburner shared memory needs matching access"; }
        catch { return "Afterburner bridge unavailable"; }
    }
    private static string ReadText(MemoryMappedViewAccessor view,long offset) {
        byte[] bytes = new byte[260]; view.ReadArray(offset,bytes,0,bytes.Length);
        int end = Array.IndexOf(bytes,(byte)0);
        return System.Text.Encoding.Latin1.GetString(bytes,0,end<0?bytes.Length:end);
    }
}
