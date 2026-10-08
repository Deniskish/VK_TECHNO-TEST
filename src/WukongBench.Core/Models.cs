using System.Text.Json;

namespace WukongBench.Core;

public sealed record SteamInstallation(string SteamDirectory, string InstallDirectory, string Executable, string BuildId);
public sealed record GpuInfo(int AdapterIndex, string Name, ulong DedicatedVideoMemoryBytes, bool? RayTracingSupported);
public sealed record HardwareInfo(string Cpu, int LogicalProcessors, ulong RamBytes, string WindowsVersion,
    int ScreenWidth, int ScreenHeight, IReadOnlyList<GpuInfo> Gpus, int SelectedAdapterIndex);

public sealed record BenchmarkProfile(string Name, int Width, int Height, int RenderScalePercent,
    int QualityLevel, bool RayTracing, int AdapterIndex, string Rationale)
{
    public bool VSync => false;
    public bool FrameGeneration => false;
    public int FpsLimit => 0;
    public static BenchmarkProfile Cpu(int adapter) => new("CPU", 1280, 720, 50, 1, false, adapter,
        "720p, 50% render scale и Low уменьшают нагрузку GPU. CPU-зависимость не гарантируется: зависит от оборудования и сцены.");
    public static BenchmarkProfile Gpu(HardwareInfo hw, GpuInfo gpu) => new("GPU",
        hw.ScreenWidth, hw.ScreenHeight, 100, 5, gpu.RayTracingSupported == true, gpu.AdapterIndex,
        "Нативное разрешение основного экрана, 100% render scale, Cinematic. RT включается при подтверждённой поддержке DXR; ограничения FPS отключены.");
}

public sealed record BenchmarkResult(double AverageFps, double MinimumFps, double MaximumFps, double? Fps95,
    int FrameRecordCount, Dictionary<string, JsonElement> ReportedSettings);

public sealed class RunReport(BenchmarkProfile settings)
{
    public BenchmarkProfile Settings { get; } = settings;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public BenchmarkResult? Results { get; set; }
    public string? RawResultFile { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; } = [];
}

public sealed class SessionReport
{
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public string Status { get; set; } = "Running";
    public HardwareInfo? Hardware { get; set; }
    public SteamInstallation? Installation { get; set; }
    public List<RunReport> Runs { get; } = [];
    public string? Error { get; set; }
    public string ConfigurationRestoreStatus { get; set; } = "Not modified";
}
