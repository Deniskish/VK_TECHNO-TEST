using System.Text;
using WukongBench.Core;

// Synthetic parser fixtures only. This project never launches a benchmark or writes a benchmark report.
var tests = new (string Name, Action Body)[]
{
    ("VDF nested libraries, escaping, comments and legacy format", () =>
    {
        var data = ValveKeyValues.Parse("\"libraryfolders\" { // comment\n \"0\" { \"path\" \"D:\\\\Steam Library\" \"apps\" { \"3132990\" \"123\" } } \"1\" \"E:\\\\Games\" }");
        Equal(@"D:\Steam Library", data.Objects["libraryfolders"].Objects["0"].Values["path"]);
        Equal(@"E:\Games", data.Objects["libraryfolders"].Values["1"]);
        Throws<FormatException>(() => ValveKeyValues.Parse("a { b \"unterminated"));
    }),
    ("INI edits are section scoped; unknown UI schema rejected", () =>
    {
        var ini = new IniDocument("; keep\n[Main]\nUISettingData=((\"Rtx\", \"1\"),(\"Other\",\"a,b\"))\nX=1\n[Other]\nX=9\n");
        ini.SetUi("Main", new Dictionary<string, string> { ["Rtx"] = "0" });
        ini.Set("Main", "X", "2");
        Equal("9", ini.Get("Other", "X")!);
        Equal("2", ini.Get("Main", "X")!);
        Assert(ini.ToString().Contains("; keep"));
        Assert(ini.ToString().Contains("\"a,b\""));
        Throws<FormatException>(() => ini.SetUi("Main", new Dictionary<string, string> { ["Missing"] = "1" }));
        Throws<FormatException>(() => new IniDocument("[x]\na=1\na=2").Get("x", "a"));
    }),
    ("History parser rejects absent scores, partial JSON and inconsistent FPS", () =>
    {
        var parser = new BenchmarkResultParser();
        Equal(80d, parser.Parse(Fixture()).AverageFps);
        Throws<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes("{\"LastCPUBenchmarkResult\":123}")));
        Throws<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes("{\"FPSAvg\":0,\"FPSMin\":1,\"FPSMax\":2}")));
        Throws<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes("{\"FPSAvg\":5,\"FPSMin\":10,\"FPSMax\":20,\"Records\":[{}]}")));
        Throws<System.Text.Json.JsonException>(() => parser.Parse(Encoding.UTF8.GetBytes("{\"FPSAvg\":80,")));
        Throws<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes("{\"FPSAvg\":80,\"FPSMin\":50,\"FPSMax\":100,\"Records\":[]}")));
        var result = parser.Parse(Fixture());
        Assert(BenchmarkResultParser.ValidateSettings(result, BenchmarkProfile.Cpu(0)).Count == 0);
        Assert(BenchmarkResultParser.ValidateSettings(result, BenchmarkProfile.Cpu(0) with { RayTracing = true }).Count > 0);
    }),
    ("Backup restores original bytes, deleted files, attributes and newly created config", () => InTemp(root =>
    {
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        var file = Path.Combine(config, "GameUserSettings.ini");
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[original]\r\nText=Привет\r\n")).ToArray();
        File.WriteAllBytes(file, bytes);
        var state = Path.Combine(root, "state");
        var transaction = new ConfigurationBackup(state, [config]); transaction.Begin();
        File.Delete(file); File.WriteAllText(Path.Combine(config, "Engine.ini"), "new");
        // Simulate another invocation recovering a persisted journal.
        new ConfigurationBackup(state, [config]).Restore();
        Assert(File.ReadAllBytes(file).SequenceEqual(bytes));
        Assert(!File.Exists(Path.Combine(config, "Engine.ini")));
        Assert(!transaction.HasPendingRecovery);
        Assert(Directory.EnumerateFiles(Path.Combine(state, "backups"), "*.bak", SearchOption.AllDirectories).Any());
        transaction.Restore(); // idempotent
    })),
    ("Corrupt backup never overwrites active config", () => InTemp(root =>
    {
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        var file = Path.Combine(config, "GameUserSettings.ini"); File.WriteAllText(file, "original");
        var state = Path.Combine(root, "state"); var backup = new ConfigurationBackup(state, [config]); backup.Begin();
        File.WriteAllText(file, "modified");
        var copy = Directory.EnumerateFiles(Path.Combine(state, "backups"), "*.bak", SearchOption.AllDirectories).Single();
        File.WriteAllText(copy, "corrupted");
        Throws<IOException>(backup.Restore); Equal("modified", File.ReadAllText(file)); Assert(backup.HasPendingRecovery);
    })),
    ("Initially missing configuration is removed on restore", () => InTemp(root =>
    {
        var config = Path.Combine(root, "config"); var backup = new ConfigurationBackup(Path.Combine(root, "state"), [config]);
        backup.Begin(); Directory.CreateDirectory(config); File.WriteAllText(Path.Combine(config, "new.ini"), "bootstrap");
        backup.Restore(); Assert(!Directory.EnumerateFiles(config).Any());
    })),
    ("History watcher rejects old files and waits for stable complete JSON", () => InTemp(root =>
    {
        var old = Path.Combine(root, "old"); File.WriteAllBytes(old, Fixture());
        var watcher = new ResultHistoryWatcher([root], new()); watcher.Begin();
        Assert(watcher.Poll() is null); Assert(watcher.Poll() is null);
        var fresh = Path.Combine(root, "fresh-without-extension"); File.WriteAllText(fresh, "{\"FPSAvg\":");
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddSeconds(1));
        Assert(watcher.Poll() is null); Assert(watcher.Poll() is null);
        File.WriteAllBytes(fresh, Fixture()); File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddSeconds(1));
        Assert(watcher.Poll() is null);
        Equal(fresh, watcher.Poll()!.Value.Path);
    }))
};
var failures = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + e); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static byte[] Fixture() => Encoding.UTF8.GetBytes("""
{"FPSAvg":80,"FPSMin":50,"FPSMax":100,"FPS95":60,"Records":[{"FrameRate":80}],
"QualityLevel":1,"ImageQuality":50,"InsertFrame":0,"Rtx":0,"ScreenResolution":"1280 × 720"}
""");
static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static void InTemp(Action<string> action)
{
    var path = Path.Combine(Path.GetTempPath(), "wukong-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
    try { action(path); } finally { Directory.Delete(path, true); }
}
