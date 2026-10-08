using System.Security.Cryptography;

namespace WukongBench.Core;

/// <summary>Accepts only changed, stable, fully parsed history files after this run's start.</summary>
public sealed class ResultHistoryWatcher(IEnumerable<string> directories, BenchmarkResultParser parser)
{
    private readonly string[] roots = directories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly Dictionary<string, string> baseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> previous = new(StringComparer.OrdinalIgnoreCase);
    private DateTime startedUtc;
    public string? LastParseError { get; private set; }

    public void Begin()
    {
        baseline.Clear(); previous.Clear();
        foreach (var path in Files())
        {
            var bytes = Read(path);
            if (bytes is not null) baseline[path] = Hash(bytes);
        }
        startedUtc = DateTime.UtcNow;
    }

    public (string Path, byte[] Bytes, BenchmarkResult Result)? Poll()
    {
        foreach (var path in Files())
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < startedUtc) continue;
                var bytes = Read(path);
                if (bytes is null) continue;
                var hash = Hash(bytes);
                if (baseline.TryGetValue(path, out var old) && old == hash) continue;
                var stable = previous.TryGetValue(path, out var last) && last == hash;
                previous[path] = hash;
                if (!stable) continue;
                return (path, bytes, parser.Parse(bytes));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
            { LastParseError = $"{path}: {e.Message}"; }
        }
        return null;
    }

    private IEnumerable<string> Files() => roots.Where(Directory.Exists).SelectMany(root =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint })).Order(StringComparer.Ordinal);

    private static byte[]? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is <= 0 or > 128 * 1024 * 1024) return null;
            using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
