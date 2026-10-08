using System.Text;

namespace WukongBench.Services;

public sealed class StageLogger(string path) : IDisposable
{
    private readonly StreamWriter writer = new(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
    public void Write(string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}";
        lock (writer) { Console.WriteLine(line); writer.WriteLine(line); }
    }
    public void Dispose() => writer.Dispose();
}
