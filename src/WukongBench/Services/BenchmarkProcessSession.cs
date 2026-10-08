using System.Diagnostics;
using WukongBench.Core;

namespace WukongBench.Services;

/// <summary>Tracks only executables under this installation and created by this session.</summary>
public sealed class BenchmarkProcessSession(SteamInstallation installation, Action<string> log) : IAsyncDisposable
{
    private readonly Dictionary<int, Process> owned = [];
    private DateTime startedUtc;
    private bool started;
    private string[] Executables => [installation.Executable,
        Path.Combine(installation.InstallDirectory, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe")];

    private IEnumerable<Process> Matching()
    {
        foreach (var name in new[] { "b1_benchmark", "b1-Win64-Shipping" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            bool matches;
            try { matches = Executables.Contains(process.MainModule?.FileName, StringComparer.OrdinalIgnoreCase); }
            catch (InvalidOperationException) { process.Dispose(); continue; }
            catch (System.ComponentModel.Win32Exception e)
            { process.Dispose(); throw new InvalidOperationException("Не удалось проверить путь запущенного b1-процесса. Закройте его или выровняйте права запуска.", e); }
            if (matches) yield return process;
            else process.Dispose();
        }
    }

    public void EnsureNotRunning()
    {
        var processes = Matching().ToArray();
        try
        {
            if (processes.Length != 0) throw new InvalidOperationException("Benchmark Tool уже запущен. Закройте его перед работой утилиты (существующие процессы не завершаются).");
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    public void Start(BenchmarkProfile? profile)
    {
        EnsureNotRunning();
        startedUtc = DateTime.UtcNow;
        var info = new ProcessStartInfo(installation.Executable)
        { UseShellExecute = false, WorkingDirectory = installation.InstallDirectory };
        foreach (var arg in new[] { "-dx12", "-culture=en", "-windowed" }) info.ArgumentList.Add(arg);
        if (profile is not null)
        {
            info.ArgumentList.Add($"-ResX={profile.Width}"); info.ArgumentList.Add($"-ResY={profile.Height}");
            info.ArgumentList.Add($"-graphicsadapter={profile.AdapterIndex}");
            info.ArgumentList.Add("-ForceRes");
            if (profile.Name == "GPU") info.ArgumentList.Add("-WindowedFullscreen");
        }
        info.Environment["SteamAppId"] = SteamLocator.AppId;
        var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить Benchmark Tool.");
        owned.Add(process.Id, process); started = true;
        log($"Запущен launcher PID {process.Id}.");
    }

    private void Refresh()
    {
        if (!started) return;
        foreach (var process in Matching())
        {
            if (!owned.ContainsKey(process.Id) && process.StartTime.ToUniversalTime() >= startedUtc.AddSeconds(-1)) owned.Add(process.Id, process);
            else process.Dispose();
        }
    }

    public nint Window()
    {
        Refresh();
        foreach (var process in owned.Values)
        {
            if (process.HasExited) continue;
            process.Refresh();
            if (process.MainWindowHandle != nint.Zero) return process.MainWindowHandle;
        }
        return nint.Zero;
    }
    public bool IsAlive { get { Refresh(); return owned.Values.Any(p => !p.HasExited); } }

    public async Task StopAsync()
    {
        if (!started) return;
        Refresh();
        foreach (var process in owned.Values.Where(p => !p.HasExited)) process.CloseMainWindow();
        var until = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < until && IsAlive) await Task.Delay(250);
        Refresh();
        foreach (var process in owned.Values)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);
        }
        if (IsAlive) throw new IOException("Процесс Benchmark Tool ещё работает; восстановление конфигурации небезопасно.");
        started = false;
    }
    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        finally { foreach (var process in owned.Values) process.Dispose(); }
    }
}
