using System.Diagnostics;
using System.Text.Json;
using WukongBench.Core;

namespace WukongBench.Services;

/// <summary>Collects local evidence for diagnosing version-dependent config/history adapters.</summary>
public sealed class ApplicationInspector
{
    public void Save(SteamInstallation installation, string output)
    {
        var roots = new[]
        {
            Path.Combine(installation.InstallDirectory, "b1", "Saved"),
            Path.Combine(installation.InstallDirectory, "b1", "BenchMarkHistory"),
            Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "b1", "Saved"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "b1", "BenchMarkHistory")
        };
        var files = roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            .Take(10000).Select(path => new FileInfo(path)).Select(f => new { f.FullName, f.Length, f.LastWriteTimeUtc }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            installation.BuildId,
            ExecutableVersion = FileVersionInfo.GetVersionInfo(installation.Executable).FileVersion,
            SearchedRoots = roots,
            Files = files
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
