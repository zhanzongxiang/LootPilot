using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TarkovPriceOverlay.Configuration;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public sealed class TesseractOcrService : IOcrService, IConfigurableOcrLayoutService
{
    private readonly AppSettings _settings;
    private readonly object _userWordsLock = new();
    public TesseractOcrService(AppSettings settings) => _settings = settings;

    public async Task<string> RecognizeAsync(Bitmap image, CancellationToken ct = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TarkovPriceOverlay");
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var input = Path.Combine(directory, id + ".png");
        image.Save(input, ImageFormat.Png);
        try
        {
            var executable = ResolveExecutable();
            var tessdata = ResolveTessdata(executable);
            var userWords = ResolveUserWords();
            var tasks = OcrLanguages(useAllLanguageOrders: true).Select(language => RunAsync(
                executable, input, tessdata, language,
                _settings.OcrPageSegmentationMode, "", userWords, ct));
            var outputs = await Task.WhenAll(tasks);
            return string.Join(Environment.NewLine, outputs.Select(x => x.Trim()).Where(x => x.Length > 0));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "未找到 Tesseract OCR。请安装 Tesseract，或在 appsettings.json 中配置 TesseractPath。");
        }
        finally
        {
            if (!_settings.DebugSaveCaptures && File.Exists(input)) File.Delete(input);
        }
    }

    public async Task<IReadOnlyList<OcrTextBlock>> RecognizeBlocksAsync(
        Bitmap image, CancellationToken ct = default)
        => await RecognizeBlocksAsync(image, useAllLanguageOrders: true, ct);

    public async Task<IReadOnlyList<OcrTextBlock>> RecognizeBlocksAsync(
        Bitmap image, bool useAllLanguageOrders, CancellationToken ct = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TarkovPriceOverlay");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
        image.Save(input, ImageFormat.Png);
        try
        {
            var executable = ResolveExecutable();
            var tessdata = ResolveTessdata(executable);
            var userWords = ResolveUserWords();
            var tasks = OcrLanguages(useAllLanguageOrders).Select(language => RunAsync(
                executable, input, tessdata, language, 11, "tsv", userWords, ct));
            var outputs = await Task.WhenAll(tasks);
            return outputs.SelectMany(ParseTsv)
                .GroupBy(x => (Normalize(x.Text), X: x.Bounds.X / 8, Y: x.Bounds.Y / 8))
                .Select(group => group.OrderByDescending(x => x.Confidence).First())
                .ToList();
        }
        finally
        {
            if (!_settings.DebugSaveCaptures && File.Exists(input)) File.Delete(input);
        }
    }

    private IReadOnlyList<string> OcrLanguages(bool useAllLanguageOrders)
    {
        var configured = _settings.TesseractLanguage.Trim();
        if (configured.Contains("eng", StringComparison.OrdinalIgnoreCase) &&
            configured.Contains("chi_sim", StringComparison.OrdinalIgnoreCase))
            return useAllLanguageOrders
                ? ["chi_sim+eng", "eng+chi_sim"]
                : ["chi_sim+eng"];
        return [configured];
    }

    private static async Task<string> RunAsync(
        string executable, string input, string tessdata, string language,
        int psm, string outputFormat, string? userWords, CancellationToken ct)
    {
        var tessdataArgument = Directory.Exists(tessdata) ? $" --tessdata-dir \"{tessdata}\"" : "";
        var formatArgument = string.IsNullOrWhiteSpace(outputFormat) ? "" : $" {outputFormat}";
        var userWordsArgument = File.Exists(userWords) ? $" --user-words \"{userWords}\"" : "";
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"\"{input}\" stdout -l {language} --psm {psm}{tessdataArgument}{userWordsArgument}{formatArgument}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 Tesseract OCR。");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output;
    }

    private string? ResolveUserWords()
    {
        var cache = _settings.ExpandedModeCachePath;
        if (!File.Exists(cache)) return null;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LootPilot");
        var mode = _settings.GameMode.ToLowerInvariant() switch
        {
            "pve" => "pve",
            "season" => "season",
            _ => "pvp"
        };
        var output = Path.Combine(directory, $"ocr-user-words-{mode}.txt");
        lock (_userWordsLock)
        {
            if (File.Exists(output) && File.GetLastWriteTimeUtc(output) >= File.GetLastWriteTimeUtc(cache))
                return output;
            try
            {
                var envelope = JsonSerializer.Deserialize<CacheEnvelope>(File.ReadAllText(cache),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (envelope?.Items.Count is not > 0) return null;
                var words = envelope.Items
                    .SelectMany(item => new[] { item.Name, item.ShortName })
                    .SelectMany(ExtractDictionaryWords)
                    .Where(word => word.Length >= 2)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(word => word, StringComparer.OrdinalIgnoreCase);
                Directory.CreateDirectory(directory);
                File.WriteAllLines(output, words);
                return output;
            }
            catch
            {
                return null;
            }
        }
    }

    private static IEnumerable<string> ExtractDictionaryWords(string value)
    {
        foreach (Match match in Regex.Matches(value,
                     @"[\u3400-\u9FFF]{2,}|[A-Za-z0-9][A-Za-z0-9.\-]{1,}"))
            yield return match.Value;
    }

    private static string Normalize(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static IReadOnlyList<OcrTextBlock> ParseTsv(string tsv)
    {
        var words = new List<(string Key, string Text, Rectangle Bounds, double Confidence)>();
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length < 12 || fields[0] != "5" || string.IsNullOrWhiteSpace(fields[11])) continue;
            if (!int.TryParse(fields[6], out var left) || !int.TryParse(fields[7], out var top) ||
                !int.TryParse(fields[8], out var width) || !int.TryParse(fields[9], out var height)) continue;
            _ = double.TryParse(fields[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence);
            words.Add(($"{fields[1]}:{fields[2]}:{fields[3]}:{fields[4]}", fields[11],
                new Rectangle(left, top, width, height), confidence / 100d));
        }
        return words.Select(x => new OcrTextBlock(x.Text, x.Bounds, x.Confidence)).ToList();
    }

    private string ResolveExecutable()
    {
        var configured = Environment.ExpandEnvironmentVariables(_settings.TesseractPath)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(configured) && File.Exists(configured)) return configured;

        var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        if (File.Exists(bundled)) return bundled;

        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Tesseract-OCR", "tesseract.exe");
        return File.Exists(installed) ? installed : "tesseract.exe";
    }

    private string ResolveTessdata(string executable)
    {
        var configured = Environment.ExpandEnvironmentVariables(_settings.TessdataPath)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(configured) && Directory.Exists(configured)) return configured;

        var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        if (Directory.Exists(bundled)) return bundled;
        return Path.Combine(Path.GetDirectoryName(executable) ?? "", "tessdata");
    }
}
