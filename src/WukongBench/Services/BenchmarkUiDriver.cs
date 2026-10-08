using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WukongBench.Interop;

namespace WukongBench.Services;

public interface IBenchmarkUiDriver
{
    Task<UiObservation> ObserveAsync(nint window, string evidenceDirectory, CancellationToken token);
    void Click(nint window, UiButton button);
    void Enter(nint window);
}
public sealed record UiButton(int X, int Y);
public sealed record UiObservation(string Text, UiButton? Start, UiButton? Confirm, bool TitlePrompt, bool ResultScreen);

/// <summary>Version-dependent English labels; no hardcoded screen coordinates or blind input loop.</summary>
public sealed class BenchmarkUiDriver : IBenchmarkUiDriver
{
    private readonly OcrEngine engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? throw new InvalidOperationException("Не установлен английский компонент Windows OCR. Добавьте English language / OCR в параметрах Windows.");

    public async Task<UiObservation> ObserveAsync(nint window, string evidenceDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        WindowInput.Activate(window);
        await Task.Delay(300, token);
        WindowInput.EnsureForeground(window);
        if (!WindowInput.GetClientRect(window, out var rect) || rect.Right < 100 || rect.Bottom < 100)
            throw new InvalidOperationException("Клиентская область Benchmark Tool недоступна.");
        var origin = new WindowInput.Point();
        if (!WindowInput.ClientToScreen(window, ref origin)) throw new InvalidOperationException("Не удалось получить положение окна.");
        var screenRect = new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom);
        if (!SystemInformation.VirtualScreen.Contains(screenRect))
            throw new InvalidOperationException("Окно Benchmark Tool выходит за границы рабочего стола; невозможно надёжно распознать меню.");
        using var bitmap = new Bitmap(rect.Right, rect.Bottom, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        WindowInput.EnsureForeground(window);
        Directory.CreateDirectory(evidenceDirectory);
        bitmap.Save(Path.Combine(evidenceDirectory, "last-screen.png"), ImageFormat.Png);
        var factor = Math.Min(1d, Math.Min(1920d, OcrEngine.MaxImageDimension) / Math.Max(bitmap.Width, bitmap.Height));
        using var scaled = new Bitmap(bitmap, new Size((int)(bitmap.Width * factor), (int)(bitmap.Height * factor)));
        using var stream = new MemoryStream();
        scaled.Save(stream, ImageFormat.Bmp); stream.Position = 0;
        using var randomAccess = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccess);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(softwareBitmap);
        token.ThrowIfCancellationRequested();
        File.WriteAllText(Path.Combine(evidenceDirectory, "last-screen.txt"), result.Text);
        UiButton? Find(params string[] labels)
        {
            var matches = result.Lines.Where(line => labels.Contains(Normalize(line.Text), StringComparer.Ordinal)).ToArray();
            if (matches.Length != 1) return null;
            var words = matches[0].Words;
            var left = words.Min(w => w.BoundingRect.Left);
            var right = words.Max(w => w.BoundingRect.Right);
            var top = words.Min(w => w.BoundingRect.Top);
            var bottom = words.Max(w => w.BoundingRect.Bottom);
            return new((int)((left + right) / 2 / factor), (int)((top + bottom) / 2 / factor));
        }
        var normalized = Normalize(result.Text);
        return new(result.Text, Find("benchmark", "start benchmark", "benchmark test"), Find("confirm"),
            normalized.Contains("press any", StringComparison.Ordinal) || normalized.Contains("press enter", StringComparison.Ordinal),
            normalized.Contains("benchmark results", StringComparison.Ordinal) || normalized.Contains("average fps", StringComparison.Ordinal));
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
    public void Click(nint window, UiButton button) => WindowInput.Click(window, button.X, button.Y);
    public void Enter(nint window) => WindowInput.Key(window, 0x0D);
}
