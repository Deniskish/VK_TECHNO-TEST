using Microsoft.Win32;
using WukongBench.Core;

namespace WukongBench.Services;

public sealed class SteamLocator(Action<string> log)
{
    public const string AppId = "3132990";

    public SteamInstallation Find()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(@"Software\Valve\Steam");
            foreach (var name in new[] { "SteamPath", "InstallPath", "SteamExe" })
                if (key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value))
                    roots.Add(Path.GetFullPath(name == "SteamExe" ? Path.GetDirectoryName(value)! : value));
        }
        if (roots.Count == 0) throw new FileNotFoundException("Steam не найден в реестре текущего пользователя или Windows. Установите и запустите Steam.");
        var failures = new List<string>();
        foreach (var root in roots)
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            foreach (var vdf in new[] { Path.Combine(root, "steamapps", "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
            {
                if (!File.Exists(vdf)) continue;
                try
                {
                    var data = ValveKeyValues.Parse(File.ReadAllText(vdf));
                    if (!data.Objects.TryGetValue("libraryfolders", out var folders)) continue;
                    foreach (var (id, folder) in folders.Objects)
                        if (uint.TryParse(id, out _) && folder.Values.TryGetValue("path", out var path)) libraries.Add(path);
                    foreach (var (id, path) in folders.Values)
                        if (uint.TryParse(id, out _)) libraries.Add(path); // older Steam format
                }
                catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
                { failures.Add($"{vdf}: {e.Message}"); }
            }
            foreach (var library in libraries)
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
                if (!File.Exists(manifest)) continue;
                try
                {
                    var state = ValveKeyValues.Parse(File.ReadAllText(manifest)).Objects["AppState"];
                    if (state.Values.GetValueOrDefault("appid") != AppId) throw new FormatException("AppId manifest не совпадает.");
                    var common = Path.GetFullPath(Path.Combine(library, "steamapps", "common"));
                    var install = Path.GetFullPath(Path.Combine(common, state.Values["installdir"]));
                    if (!install.StartsWith(common + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("Некорректный installdir.");
                    var exe = Path.Combine(install, "b1_benchmark.exe");
                    if (!File.Exists(exe)) throw new FileNotFoundException("Нет b1_benchmark.exe. Проверьте целостность файлов в Steam.");
                    log($"Найдена установка: {install}");
                    return new(root, install, exe, state.Values.GetValueOrDefault("buildid", "unknown"));
                }
                catch (Exception e) when (e is IOException or FormatException or KeyNotFoundException or UnauthorizedAccessException)
                { failures.Add($"{manifest}: {e.Message}"); }
            }
        }
        throw new FileNotFoundException("Black Myth: Wukong Benchmark Tool (Steam AppId 3132990) не найден в библиотеках Steam. " +
            "Установите бесплатный Benchmark Tool; основная игра не подходит. " + string.Join("; ", failures));
    }
}
