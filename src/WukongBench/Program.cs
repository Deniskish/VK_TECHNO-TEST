using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using WukongBench.Core;
using WukongBench.Services;

namespace WukongBench;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Contains("--help"))
        {
            Console.WriteLine("WukongBench [--output <directory>] [--timeout-minutes 1..120] [--recover-only]\n" +
                "Без параметров выполняет CPU и GPU benchmark. Требуется активный рабочий стол Windows и Steam.");
            return 0;
        }
        try { return RunAsync(args).GetAwaiter().GetResult(); }
        catch (Exception e) { Console.Error.WriteLine("Ошибка: " + e.Message); return 1; }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) throw new PlatformNotSupportedException("Требуется Windows 10 2004 / Windows 11.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WukongBench");
        var output = Path.Combine(root, "reports");
        var minutes = 20;
        var recoverOnly = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length: output = Path.GetFullPath(args[++i]); break;
                case "--timeout-minutes" when i + 1 < args.Length && int.TryParse(args[++i], out minutes) && minutes is >= 1 and <= 120: break;
                case "--recover-only": recoverOnly = true; break;
                default: throw new ArgumentException("Неизвестный аргумент или недопустимое значение. Используйте --help.");
            }
        }
        var directory = Path.Combine(output, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        using var logger = new StageLogger(Path.Combine(directory, "run.log"));
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        var report = new SessionReport();
        ConfigurationBackup? backup = null;
        BenchmarkProcessSession? guard = null;
        FileStream? sessionLock = null;
        var exit = 1;
        try
        {
            logger.Write("Поиск Steam и Benchmark Tool.");
            var installation = new SteamLocator(logger.Write).Find();
            report.Installation = installation;
            var state = Path.Combine(root, "state", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installation.InstallDirectory.ToUpperInvariant())))[..16]);
            Directory.CreateDirectory(state);
            try { sessionLock = new FileStream(Path.Combine(state, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException e) { throw new IOException("Другая копия утилиты уже работает с этой установкой.", e); }
            guard = new BenchmarkProcessSession(installation, logger.Write);
            guard.EnsureNotRunning();
            var config = new BenchmarkConfigManager(installation.InstallDirectory, logger.Write);
            backup = new ConfigurationBackup(state, config.ConfigDirectories);
            if (backup.HasPendingRecovery)
            {
                logger.Write("Обнаружен незавершённый запуск; восстанавливаю сохранённую конфигурацию.");
                backup.Restore(); report.ConfigurationRestoreStatus = "Recovered previous session";
            }
            if (recoverOnly) { report.Status = "Recovered"; exit = 0; }
            else
            {
                _ = config.FindSettings(); // Reject ambiguous installations before any configuration writes.
                new ApplicationInspector().Save(installation, Path.Combine(directory, "installation-before.json"));
                report.Hardware = new HardwareInfoProvider().Read();
                var gpu = report.Hardware.Gpus.Single(g => g.AdapterIndex == report.Hardware.SelectedAdapterIndex);
                report.Runs.Add(new(BenchmarkProfile.Cpu(gpu.AdapterIndex)));
                report.Runs.Add(new(BenchmarkProfile.Gpu(report.Hardware, gpu)));
                if (gpu.RayTracingSupported is null) report.Runs[1].Warnings.Add("DXR capability неизвестна: RT отключён.");
                var ui = new BenchmarkUiDriver(); // Verify OCR availability before touching configuration.
                await EnsureSteamAsync(installation, cancellation.Token);
                backup.Begin(); report.ConfigurationRestoreStatus = "Pending";
                logger.Write($"Backup и журнал записаны: {state}");
                var runner = new BenchmarkRunner(installation, ui, logger.Write, TimeSpan.FromMinutes(minutes));
                if (config.FindSettings() is null) await runner.BootstrapAsync(Path.Combine(directory, "bootstrap"), cancellation.Token);
                foreach (var run in report.Runs)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var runDirectory = Path.Combine(directory, run.Settings.Name.ToLowerInvariant());
                    try
                    {
                        guard.EnsureNotRunning();
                        config.Apply(run.Settings, runDirectory);
                        await runner.RunAsync(run, runDirectory, cancellation.Token);
                        if (run.Results?.ReportedSettings.TryGetValue("GPUModel", out var actualGpu) == true &&
                            !string.Equals(actualGpu.ToString().Trim(), gpu.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Приложение использовало другой GPU: {actualGpu}; ожидался {gpu.Name}.");
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        run.Status = "Failed"; run.Error = e.Message;
                        logger.Write($"Ошибка {run.Settings.Name}: {e.Message}");
                        guard.EnsureNotRunning(); // Continue to second test only after confirmed shutdown.
                    }
                    finally { if (Directory.Exists(runDirectory)) config.SaveEffectiveConfig(runDirectory); }
                }
                report.Status = report.Runs.All(r => r.Status == "Completed") ? "Completed" : "Failed";
                new ApplicationInspector().Save(installation, Path.Combine(directory, "installation-after.json"));
                exit = report.Status == "Completed" ? 0 : 1;
            }
        }
        catch (Exception e)
        {
            report.Status = e is OperationCanceledException ? "Cancelled" : "Failed";
            report.Error = e.ToString(); exit = e is OperationCanceledException ? 130 : 1;
            logger.Write(e.Message);
        }
        finally
        {
            try
            {
                if (backup?.HasPendingRecovery == true)
                {
                    guard?.EnsureNotRunning();
                    backup.Restore(); report.ConfigurationRestoreStatus = "Restored";
                    logger.Write("Исходная конфигурация восстановлена. Backup сохранён.");
                }
            }
            catch (Exception e)
            {
                report.ConfigurationRestoreStatus = "FAILED: " + e.Message;
                report.Status = "Failed"; exit = 2;
                logger.Write("Восстановление не выполнено: " + e.Message + ". Журнал и backup сохранены; закройте Benchmark Tool и выполните --recover-only.");
            }
            sessionLock?.Dispose();
            if (guard is not null) await guard.DisposeAsync();
            Console.CancelKeyPress -= cancelHandler;
            foreach (var run in report.Runs.Where(r => r.Status == "Pending")) run.Status = "NotRun";
            report.FinishedUtc = DateTimeOffset.UtcNow;
            new ReportWriter().Write(report, directory);
        }
        return exit;
    }

    private static async Task EnsureSteamAsync(SteamInstallation installation, CancellationToken token)
    {
        bool Running()
        {
            var processes = Process.GetProcessesByName("steam");
            try { return processes.Any(p => !p.HasExited); }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        if (Running()) return;
        using var steam = Process.Start(new ProcessStartInfo(Path.Combine(installation.SteamDirectory, "steam.exe"), "-silent") { UseShellExecute = true });
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(2000, token);
            if (Running()) { await Task.Delay(5000, token); return; }
        }
        throw new TimeoutException("Steam не запустился. Требуется установленный Steam с выполненным входом в аккаунт.");
    }
}
