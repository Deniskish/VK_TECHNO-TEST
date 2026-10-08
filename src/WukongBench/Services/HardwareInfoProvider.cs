using System.Runtime.InteropServices;
using Microsoft.Win32;
using WukongBench.Core;
using WukongBench.Interop;

namespace WukongBench.Services;

public sealed class HardwareInfoProvider
{
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalKilobytes);

    public HardwareInfo Read()
    {
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        using var os = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Не найден активный основной экран.");
        if (!GetPhysicallyInstalledSystemMemory(out var memory)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var gpus = GraphicsProbe.Read();
        var selected = gpus.OrderByDescending(g => g.DedicatedVideoMemoryBytes).FirstOrDefault()
            ?? throw new InvalidOperationException("DXGI не обнаружил аппаратный графический адаптер.");
        // ProductName may still say Windows 10 on Windows 11; always include the actual build.
        var version = $"{os?.GetValue("ProductName")} {os?.GetValue("DisplayVersion")} build {os?.GetValue("CurrentBuildNumber")}.{os?.GetValue("UBR")} ({RuntimeInformation.OSArchitecture})";
        return new(cpu?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown", Environment.ProcessorCount,
            memory * 1024, version, bounds.Width, bounds.Height, gpus, selected.AdapterIndex);
    }
}
