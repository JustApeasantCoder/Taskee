# Component notices

The XAML Diagnostics COM attachment plumbing is adapted from [TaskbarWidgets](https://github.com/pfcdev/TaskbarWidgets), copyright (c) 2026 PFC, under MIT. The complete notice is in `third_party/TaskbarWidgets-LICENSE`.

The following unmodified NuGet libraries are included. Corresponding source is available free of charge at the linked repositories/commits under the stated licenses. MPL-2.0 text is in `third_party/MPL-2.0-LICENSE.txt`.

| Library | Version | License | Corresponding source |
| --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0; copyright LibreHardwareMonitor contributors | [source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701) |
| BlackSharp.Core | 1.0.7 | MPL-2.0; copyright Florian K. | [source](https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0) |
| DiskInfoToolkit | 1.1.2 | MPL-2.0; copyright Florian K. | [source](https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea) |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0; copyright Florian K. | [source](https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce) |
| HidSharp | 2.6.4 | Apache-2.0; copyright 2010–2025 James F. Bellinger | [source](https://github.com/IntergatedCircuits/HidSharp) |
| Mono.Posix.NETStandard | 1.0.0 | MIT; copyright Mono contributors/Microsoft | [source](https://github.com/mono/mono) |
| System.Management, System.CodeDom | 10.0.2 | MIT; copyright .NET Foundation and contributors | [source](https://github.com/dotnet/runtime/tree/v10.0.2) |
| System.IO.Ports, System.Threading.AccessControl | 10.0.3 | MIT; copyright .NET Foundation and contributors | [source](https://github.com/dotnet/runtime/tree/v10.0.3) |

HidSharp's full notice is `third_party/HidSharp-LICENSE.txt`, and Mono's is `third_party/Mono-LICENSE.txt`. Portable builds include the .NET runtime, WPF and Windows Forms under MIT and their associated third-party notices in `third_party`.

MSI Afterburner is not included. Its documented monitoring shared-memory format is consumed read-only by independently written code; no MSI SDK source is redistributed. The optional PawnIO driver is not bundled or installed by Taskee.

Research files are excluded from builds and distributions. No GPL Windhawk mod source is incorporated into Taskee.
