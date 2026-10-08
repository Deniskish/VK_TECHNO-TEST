using System.Text;
using System.Text.Json;
using WukongBench.Core;

namespace WukongBench.Services;

public sealed class ReportWriter
{
    public void Write(SessionReport report, string directory)
    {
        var text = Format(report);
        Directory.CreateDirectory(directory);
        ConfigurationBackup.AtomicWrite(Path.Combine(directory, "report.json"), JsonSerializer.SerializeToUtf8Bytes(report,
            new JsonSerializerOptions { WriteIndented = true }));
        ConfigurationBackup.AtomicWrite(Path.Combine(directory, "report.txt"), Encoding.UTF8.GetBytes(text));
        Console.WriteLine(text);
        Console.WriteLine($"Отчёт: {directory}");
    }

    private static string Format(SessionReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("Black Myth: Wukong Benchmark automation");
        text.AppendLine($"Status: {report.Status}; UTC: {report.StartedUtc:O} — {report.FinishedUtc:O}");
        text.AppendLine($"Configuration restoration: {report.ConfigurationRestoreStatus}");
        if (report.Error is not null) text.AppendLine("Error: " + report.Error);
        text.AppendLine("\nHardware information");
        if (report.Hardware is { } hw)
        {
            text.AppendLine($"CPU: {hw.Cpu}; logical processors: {hw.LogicalProcessors}");
            text.AppendLine($"RAM: {hw.RamBytes / 1073741824d:F2} GiB");
            text.AppendLine($"Windows: {hw.WindowsVersion}");
            text.AppendLine($"Primary screen: {hw.ScreenWidth}x{hw.ScreenHeight}");
            foreach (var gpu in hw.Gpus) text.AppendLine($"GPU [{gpu.AdapterIndex}]: {gpu.Name}; VRAM: {gpu.DedicatedVideoMemoryBytes / 1073741824d:F2} GiB; DXR: {gpu.RayTracingSupported?.ToString() ?? "unknown"}");
            text.AppendLine($"Requested adapter: {hw.SelectedAdapterIndex}");
        }
        else text.AppendLine("Unavailable");
        foreach (var run in report.Runs)
        {
            var p = run.Settings;
            text.AppendLine($"\n{p.Name} benchmark settings");
            text.AppendLine($"{p.Width}x{p.Height}; render scale {p.RenderScalePercent}%; quality {(p.QualityLevel == 1 ? "Low" : "Cinematic")}; RT {(p.RayTracing ? "ON (level 2)" : "OFF")}");
            text.AppendLine("VSync OFF; FPS limit OFF; Frame Generation OFF; Motion Blur OFF");
            text.AppendLine(p.Rationale);
            text.AppendLine($"{p.Name} benchmark results: {run.Status}");
            if (run.Results is { } r)
            {
                text.AppendLine($"FPS average: {r.AverageFps:F2}; minimum: {r.MinimumFps:F2}; maximum: {r.MaximumFps:F2}; FPS95 (source field): {r.Fps95?.ToString("F2") ?? "N/A"}");
                text.AppendLine($"Frame records: {r.FrameRecordCount}; raw source copy: {run.RawResultFile}");
            }
            else text.AppendLine("FPS: N/A — измеренный результат не получен.");
            if (run.Error is not null) text.AppendLine("Error: " + run.Error);
            foreach (var warning in run.Warnings) text.AppendLine("Note: " + warning);
        }
        return text.ToString();
    }
}
