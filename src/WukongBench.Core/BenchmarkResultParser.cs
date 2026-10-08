using System.Globalization;
using System.Text.Json;

namespace WukongBench.Core;

/// <summary>Version-specific history schema. Never reads Unreal synthetic CPU/GPU scores as FPS.</summary>
public sealed class BenchmarkResultParser
{
    public BenchmarkResult Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        double Required(string name)
        {
            if (!root.TryGetProperty(name, out var property) || !TryNumber(property, out var value) || value <= 0)
                throw new FormatException($"Результат не содержит корректный {name}.");
            return value;
        }
        var avg = Required("FPSAvg");
        var min = Required("FPSMin");
        var max = Required("FPSMax");
        if (min > avg || avg > max) throw new FormatException("Некорректный порядок min/avg/max FPS.");
        if (!root.TryGetProperty("Records", out var records) || records.ValueKind != JsonValueKind.Array || records.GetArrayLength() == 0)
            throw new FormatException("В результате нет покадровых Records: файл не завершён или формат изменился.");
        double? fps95 = null;
        if (root.TryGetProperty("FPS95", out var low) && TryNumber(low, out var lowValue) && lowValue > 0)
            fps95 = lowValue;
        var settings = root.EnumerateObject().Where(p => p.Name != "Records")
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        return new(avg, min, max, fps95, records.GetArrayLength(), settings);
    }

    public static bool TryNumber(JsonElement value, out double number)
    {
        number = double.NaN;
        var parsed = value.ValueKind == JsonValueKind.Number ? value.TryGetDouble(out number) :
            value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number);
        return parsed && double.IsFinite(number);
    }

    public static IReadOnlyList<string> ValidateSettings(BenchmarkResult result, BenchmarkProfile profile)
    {
        var errors = new List<string>();
        void Check(string name, int expected)
        {
            if (!result.ReportedSettings.TryGetValue(name, out var value) || !TryNumber(value, out var actual))
                errors.Add($"Результат не подтверждает настройку {name}.");
            else if (actual != expected) errors.Add($"{name}: ожидалось {expected}, приложение сообщило {actual}.");
        }
        Check("QualityLevel", profile.QualityLevel);
        Check("ImageQuality", profile.RenderScalePercent);
        Check("InsertFrame", 0);
        Check("Rtx", profile.RayTracing ? 1 : 0);
        foreach (var key in new[] { "Vsync", "LockFrameRate", "MotionBlur" })
            if (result.ReportedSettings.ContainsKey(key)) Check(key, 0);
        foreach (var key in new[] { "ViewDistance", "AntiAliasing", "PostProcessing", "ShadowQuality", "TextureQuality",
                     "FxQuality", "MaterialQuality", "VegetationQuality", "GlobalIllumination", "ReflectionQuality" })
            if (result.ReportedSettings.ContainsKey(key)) Check(key, profile.QualityLevel);
        if (!result.ReportedSettings.TryGetValue("ScreenResolution", out var resolution) ||
            !System.Text.RegularExpressions.Regex.IsMatch(resolution.ToString(),
                $@"^\s*{profile.Width}\s*[xX×*]\s*{profile.Height}\s*$"))
            errors.Add($"Результат не подтверждает разрешение {profile.Width}x{profile.Height}.");
        return errors;
    }
}
