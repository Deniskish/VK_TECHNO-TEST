using System.Security.Cryptography;
using System.Text.Json;

namespace WukongBench.Core;

/// <summary>Durable write-ahead journal. Restoration is idempotent and original bytes are retained.</summary>
public sealed class ConfigurationBackup(string stateDirectory, IReadOnlyList<string> configDirectories)
{
    private string JournalPath => Path.Combine(stateDirectory, "active.json");
    public bool HasPendingRecovery => File.Exists(JournalPath);
    public sealed record Entry(string RelativePath, string BackupName, string Sha256, FileAttributes Attributes, DateTime LastWriteUtc);
    public sealed record DirectorySnapshot(string Directory, bool Existed, List<Entry> Entries);
    public sealed record Journal(string BackupDirectory, List<DirectorySnapshot> Snapshots);

    public void Begin()
    {
        if (HasPendingRecovery) throw new InvalidOperationException("Сначала необходимо восстановить предыдущую конфигурацию.");
        Directory.CreateDirectory(stateDirectory);
        var backupDir = Path.Combine(stateDirectory, "backups", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);
        var snapshots = new List<DirectorySnapshot>();
        foreach (var directory in configDirectories)
        {
            var snapshot = new DirectorySnapshot(Path.GetFullPath(directory), Directory.Exists(directory), []);
            if (snapshot.Existed)
            {
                foreach (var file in Enumerate(directory))
                {
                    var bytes = File.ReadAllBytes(file);
                    var name = Guid.NewGuid().ToString("N") + ".bak";
                    WriteDurable(Path.Combine(backupDir, name), bytes);
                    snapshot.Entries.Add(new(Path.GetRelativePath(directory, file), name, Hash(bytes), File.GetAttributes(file), File.GetLastWriteTimeUtc(file)));
                }
            }
            snapshots.Add(snapshot);
        }
        var journal = JsonSerializer.SerializeToUtf8Bytes(new Journal(backupDir, snapshots), new JsonSerializerOptions { WriteIndented = true });
        // Never modify application files before the journal is durably committed.
        if (File.Exists(JournalPath + ".tmp")) File.Delete(JournalPath + ".tmp");
        WriteDurable(JournalPath + ".tmp", journal);
        File.Move(JournalPath + ".tmp", JournalPath);
        File.WriteAllBytes(Path.Combine(backupDir, "manifest.json"), journal);
    }

    public void Restore()
    {
        if (!HasPendingRecovery) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(JournalPath)) ?? throw new IOException("Повреждён журнал восстановления.");
        // Validate all backup bytes and paths before touching any configuration.
        if (!Path.GetFullPath(journal.BackupDirectory).StartsWith(Path.GetFullPath(stateDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Некорректный путь backup.");
        foreach (var snapshot in journal.Snapshots)
        {
            if (!configDirectories.Any(d => string.Equals(Path.GetFullPath(d), snapshot.Directory, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Журнал ссылается на другую установку.");
            foreach (var entry in snapshot.Entries)
            {
                _ = Target(snapshot.Directory, entry.RelativePath);
                if (Path.GetFileName(entry.BackupName) != entry.BackupName ||
                    Hash(File.ReadAllBytes(Path.Combine(journal.BackupDirectory, entry.BackupName))) != entry.Sha256)
                    throw new IOException("Контрольная сумма backup не совпала. Оригинал сохранён; требуется ручное восстановление.");
            }
        }
        foreach (var snapshot in journal.Snapshots)
        {
            if (Directory.Exists(snapshot.Directory))
                foreach (var file in Enumerate(snapshot.Directory))
                    if (!snapshot.Entries.Any(e => string.Equals(Target(snapshot.Directory, e.RelativePath), file, StringComparison.OrdinalIgnoreCase)))
                    { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            foreach (var entry in snapshot.Entries)
            {
                var target = Target(snapshot.Directory, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                AtomicWrite(target, File.ReadAllBytes(Path.Combine(journal.BackupDirectory, entry.BackupName)));
                File.SetLastWriteTimeUtc(target, entry.LastWriteUtc);
                File.SetAttributes(target, entry.Attributes);
            }
        }
        File.Delete(JournalPath); // Backups remain available after success.
    }

    public static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteDurable(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void WriteDurable(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
    private static string Target(string root, string relative)
    {
        var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Недопустимый путь в журнале.");
        return result;
    }
    private static IEnumerable<string> Enumerate(string root) => Directory.EnumerateFiles(root, "*", new EnumerationOptions
        { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint });
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
