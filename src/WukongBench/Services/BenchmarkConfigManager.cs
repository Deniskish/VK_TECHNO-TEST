using System.Globalization;
using System.Text;
using WukongBench.Core;

namespace WukongBench.Services;

/// <summary>All undocumented Wukong INI conventions are isolated here.</summary>
public sealed class BenchmarkConfigManager(string installDirectory, Action<string> log)
{
    public const string SettingsSection = "/Script/GSGameSettings.GSGameUserSettings";
    public string[] ConfigDirectories { get; } =
    [
        Path.Combine(installDirectory, "b1", "Saved", "Config", "Windows"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "b1", "Saved", "Config", "Windows")
    ];
    private static readonly string[] QualityKeys = ["ViewDistance", "AntiAliasing", "PostProcessing", "ShadowQuality",
        "TextureQuality", "FxQuality", "MaterialQuality", "VegetationQuality", "GlobalIllumination", "ReflectionQuality"];
    private static readonly string[] Groups = ["ViewDistance", "AntiAliasing", "Shadow", "GlobalIllumination", "Reflection",
        "PostProcess", "Texture", "Effects", "Foliage", "Shading"];

    public string? FindSettings()
    {
        var candidates = ConfigDirectories.Select(d => Path.Combine(d, "GameUserSettings.ini")).Where(File.Exists).ToArray();
        if (candidates.Length > 1) throw new IOException("Обнаружено несколько GameUserSettings.ini (установка и LocalAppData/b1). " +
            "Нельзя безопасно определить активный конфиг; возможно, установленная полная игра использует второй файл. " + string.Join(", ", candidates));
        return candidates.SingleOrDefault();
    }

    public void Apply(BenchmarkProfile profile, string evidenceDirectory)
    {
        var path = FindSettings() ?? throw new FileNotFoundException("Приложение не создало GameUserSettings.ini.");
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        var ini = new IniDocument(reader.ReadToEnd());
        var encoding = reader.CurrentEncoding;
        reader.Close();
        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        var ui = QualityKeys.ToDictionary(k => k, _ => N(profile.QualityLevel));
        ui["QualityLevel"] = N(profile.QualityLevel);
        ui["Vsync"] = "0"; ui["LockFrameRate"] = "0"; ui["InsertFrame"] = "0";
        ui["Rtx"] = profile.RayTracing ? "1" : "0";
        ui["MotionBlur"] = "0";
        ui["ScreenMode"] = profile.Name == "CPU" ? "2" : "1";
        // This field is render height in INI, but percent in the history JSON.
        ui["ImageQuality"] = N(profile.Height * profile.RenderScalePercent / 100);
        ui["WindowFullImageQuality"] = "0";
        ui["SuperResolutionSampling"] = "3";
        ini.SetUi(SettingsSection, ui);
        if (profile.RayTracing) ini.SetUi(SettingsSection, new Dictionary<string, string> { ["RtxLevel"] = "2" });
        foreach (var key in new[] { "ResolutionSizeX", "LastUserConfirmedResolutionSizeX" }) ini.Set(SettingsSection, key, N(profile.Width));
        foreach (var key in new[] { "ResolutionSizeY", "LastUserConfirmedResolutionSizeY" }) ini.Set(SettingsSection, key, N(profile.Height));
        // Resolution enum in UISettingData is monitor-dependent; explicit engine values and launch arguments are used instead.
        foreach (var key in new[] { "FullscreenMode", "LastConfirmedFullscreenMode", "PreferredFullscreenMode" })
            ini.Set(SettingsSection, key, profile.Name == "CPU" ? "2" : "1");
        ini.Set(SettingsSection, "bUseVSync", "False");
        ini.Set(SettingsSection, "bUseDynamicResolution", "False");
        ini.Set(SettingsSection, "FrameRateLimit", "0.000000");
        foreach (var group in Groups) ini.Set("ScalabilityGroups", "sg." + group + "Quality", N(profile.QualityLevel - 1));
        ini.Set("ScalabilityGroups", "sg.ResolutionQuality", N(profile.RenderScalePercent));
        ini.Set("/Script/Engine.GameUserSettings", "bUseVSync", "False");
        var enginePath = Path.Combine(Path.GetDirectoryName(path)!, "Engine.ini");
        var engine = new IniDocument(File.Exists(enginePath) ? File.ReadAllText(enginePath) : "");
        engine.Set("Internationalization", "Culture", "en");
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["r.VSync"] = "0", ["t.MaxFPS"] = "0", ["r.DynamicRes.OperationMode"] = "0",
            ["r.ScreenPercentage"] = N(profile.RenderScalePercent), ["r.GraphicsAdapter"] = N(profile.AdapterIndex),
            ["r.RayTracing.EnableInGame"] = profile.RayTracing ? "True" : "False",
            ["r.Streamline.DLSSG.Enable"] = "0", ["r.FidelityFX.FI.Enabled"] = "0"
        }) engine.Set("SystemSettings", key, value);
        Write(path, ini.ToString(), encoding);
        Write(enginePath, engine.ToString(), new UTF8Encoding(false));
        Directory.CreateDirectory(evidenceDirectory);
        File.Copy(path, Path.Combine(evidenceDirectory, "requested-GameUserSettings.ini"), true);
        File.Copy(enginePath, Path.Combine(evidenceDirectory, "requested-Engine.ini"), true);
        log($"{profile.Name}: {profile.Width}x{profile.Height}, scale {profile.RenderScalePercent}%, quality {profile.QualityLevel}, RT {profile.RayTracing}");
    }

    public void SaveEffectiveConfig(string evidenceDirectory)
    {
        if (FindSettings() is { } path) File.Copy(path, Path.Combine(evidenceDirectory, "after-run-GameUserSettings.ini"), true);
    }

    private static void Write(string path, string text, Encoding encoding)
    {
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        ConfigurationBackup.AtomicWrite(path, bytes);
        if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException($"Не удалось проверить запись {path}.");
    }
}
