using System.Diagnostics;
using WukongBench.Core;

namespace WukongBench.Services;

public sealed class BenchmarkRunner(SteamInstallation installation, IBenchmarkUiDriver ui, Action<string> log, TimeSpan timeout)
{
    private IEnumerable<string> HistoryDirectories()
    {
        foreach (var root in new[] { Path.GetTempPath(), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), installation.InstallDirectory })
        {
            yield return Path.Combine(root, "b1", "BenchMarkHistory", "Tool");
            yield return Path.Combine(root, "b1", "Saved", "BenchMarkHistory", "Tool");
        }
    }

    public async Task BootstrapAsync(string directory, CancellationToken token)
    {
        log("Первый запуск: ожидаю создания настроек приложением.");
        await using var process = new BenchmarkProcessSession(installation, log);
        process.Start(null);
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();
            var window = process.Window();
            if (window != nint.Zero)
            {
                var screen = await ui.ObserveAsync(window, directory, token);
                if (screen.Start is not null) { await process.StopAsync(); return; }
                if (screen.TitlePrompt) ui.Enter(window);
            }
            if (watch.Elapsed > TimeSpan.FromSeconds(30) && !process.IsAlive) throw new IOException("Приложение завершилось при первичной настройке.");
            await Task.Delay(2000, token);
        }
        throw new TimeoutException("Не удалось автоматически пройти первый запуск; см. bootstrap/last-screen.png.");
    }

    public async Task RunAsync(RunReport report, string directory, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        report.Status = "Running"; report.StartedUtc = DateTimeOffset.UtcNow;
        var history = new ResultHistoryWatcher(HistoryDirectories(), new BenchmarkResultParser());
        history.Begin();
        await using var process = new BenchmarkProcessSession(installation, log);
        var watch = Stopwatch.StartNew();
        var phase = "Menu";
        var sawTitle = false;
        try
        {
            process.Start(report.Settings);
            while (watch.Elapsed < timeout)
            {
                token.ThrowIfCancellationRequested();
                var result = history.Poll();
                if (result is not null && phase is "Confirm" or "Running")
                {
                    report.Results = result.Value.Result;
                    report.RawResultFile = Path.Combine(directory, "result.raw.json");
                    File.WriteAllBytes(report.RawResultFile, result.Value.Bytes);
                    File.WriteAllText(Path.Combine(directory, "result-source.txt"), result.Value.Path);
                    var mismatches = BenchmarkResultParser.ValidateSettings(result.Value.Result, report.Settings);
                    report.Warnings.AddRange(mismatches);
                    report.Warnings.Add("VSync/FPS limit проверены в записанной конфигурации; драйверные ограничения и фактическое применение этих двух параметров JSON не подтверждает.");
                    if (mismatches.Count > 0) throw new InvalidOperationException("Получены реальные FPS, но приложение не подтвердило запрошенный профиль: " + string.Join(" ", mismatches));
                    var handle = process.Window();
                    if (handle != nint.Zero)
                    {
                        try { await ui.ObserveAsync(handle, directory, token); }
                        catch (Exception e) when (e is not OperationCanceledException) { report.Warnings.Add("Не удалось сохранить финальный экран: " + e.Message); }
                    }
                    report.Status = "Completed";
                    log($"{report.Settings.Name}: average {report.Results.AverageFps:F2} FPS; min {report.Results.MinimumFps:F2}; max {report.Results.MaximumFps:F2}.");
                    return;
                }
                var window = process.Window();
                if (window != nint.Zero && phase != "Running")
                {
                    var screen = await ui.ObserveAsync(window, directory, token);
                    if (phase == "Menu")
                    {
                        if (screen.Start is { } button && !screen.ResultScreen)
                        { ui.Click(window, button); phase = "Confirm"; log("Нажата кнопка Benchmark; ожидаю подтверждения."); }
                        else if (screen.TitlePrompt && !sawTitle) { ui.Enter(window); sawTitle = true; }
                    }
                    else if (screen.Confirm is { } confirm)
                    { ui.Click(window, confirm); phase = "Running"; log("Benchmark подтверждён; ожидаю полного JSON результата."); }
                    else if (screen.Start is null && !screen.TitlePrompt && !screen.ResultScreen)
                    { /* Loading or benchmark without confirmation; keep observing until history appears. */ }
                }
                if (watch.Elapsed > TimeSpan.FromSeconds(30) && !process.IsAlive)
                    throw new IOException("Benchmark Tool завершился до получения результата (возможен сбой или проблема Steam/DRM).");
                await Task.Delay(2000, token);
            }
            throw new TimeoutException($"Нет подтверждённого результата за {timeout.TotalMinutes:F0} мин. Этап: {phase}. " + history.LastParseError);
        }
        catch (Exception e)
        {
            report.Status = e is OperationCanceledException ? "Cancelled" : "Failed";
            report.Error = e.Message;
            if (e is not OperationCanceledException)
            {
                try
                {
                    var handle = process.Window();
                    if (handle != nint.Zero) await ui.ObserveAsync(handle, directory, token);
                }
                catch (Exception captureError) { report.Warnings.Add("Диагностический экран недоступен: " + captureError.Message); }
            }
            throw;
        }
        finally
        {
            await process.StopAsync();
            report.FinishedUtc = DateTimeOffset.UtcNow;
        }
    }
}
