using System.Buffers;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using RapidOcrNet;
using SkiaSharp;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

/// <summary>
/// PP-OCRv6-backed OCR for small Chinese game text. Tesseract remains available
/// through <see cref="HybridOcrService"/> as a compatibility fallback.
/// </summary>
public sealed class PaddleOcrService : IOcrService, IOcrLayoutService, IDisposable
{
    private const int EngineCount = 2;
    private readonly Lazy<RapidOcr>[] _engines;
    private readonly ConcurrentQueue<int> _availableEngines = new();
    private readonly SemaphoreSlim _gate = new(EngineCount, EngineCount);

    public PaddleOcrService()
    {
        _engines = Enumerable.Range(0, EngineCount)
            .Select(_ => new Lazy<RapidOcr>(CreateEngine, true))
            .ToArray();
        foreach (var index in Enumerable.Range(0, EngineCount))
            _availableEngines.Enqueue(index);
    }

    public async Task<string> RecognizeAsync(Bitmap image, CancellationToken ct = default)
    {
        var blocks = await RecognizeBlocksAsync(image, ct);
        return string.Join(Environment.NewLine, blocks.Select(x => x.Text));
    }

    public async Task<IReadOnlyList<OcrTextBlock>> RecognizeBlocksAsync(
        Bitmap image, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        if (!_availableEngines.TryDequeue(out var engineIndex))
        {
            _gate.Release();
            throw new InvalidOperationException("OCR 引擎池状态异常。");
        }
        try
        {
            using var bitmap = ToSkBitmap(image);
            var options = RapidOcrOptions.PPOCRv6 with
            {
                DoAngle = false,
                TextScore = 0.45f,
                ReturnWordBox = false
            };
            var detectorSize = Math.Max(bitmap.Width, bitmap.Height);
            if (detectorSize <= 1400)
                options = options with
                {
                    ImgResize = detectorSize,
                    LimitSideLen = detectorSize,
                    MaxSideLen = detectorSize
                };
            var result = await Task.Run(() => _engines[engineIndex].Value.Detect(bitmap, options), ct);

            return result.TextBlocks
                .Where(block => !string.IsNullOrWhiteSpace(block.Text))
                .Select(block =>
                {
                    var left = (int)Math.Floor((double)block.BoxPoints.Min(point => point.X));
                    var top = (int)Math.Floor((double)block.BoxPoints.Min(point => point.Y));
                    var right = (int)Math.Ceiling((double)block.BoxPoints.Max(point => point.X));
                    var bottom = (int)Math.Ceiling((double)block.BoxPoints.Max(point => point.Y));
                    var confidence = block.CharScores is { Length: > 0 }
                        ? block.CharScores.Average()
                        : 0d;
                    return new OcrTextBlock(block.Text,
                        Rectangle.FromLTRB(left, top, right, bottom), confidence);
                })
                .ToList();
        }
        finally
        {
            _availableEngines.Enqueue(engineIndex);
            _gate.Release();
        }
    }

    private static SKBitmap ToSkBitmap(Bitmap image)
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var source = image.LockBits(
            new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytesPerRow = image.Width * 4;
            var row = ArrayPool<byte>.Shared.Rent(bytesPerRow);
            var destination = bitmap.GetPixels();
            try
            {
                for (var y = 0; y < image.Height; y++)
                {
                    var sourceRow = IntPtr.Add(source.Scan0, y * source.Stride);
                    var destinationRow = IntPtr.Add(destination, y * bitmap.RowBytes);
                    Marshal.Copy(sourceRow, row, 0, bytesPerRow);
                    Marshal.Copy(row, 0, destinationRow, bytesPerRow);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(row);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            image.UnlockBits(source);
        }
    }

    private static RapidOcr CreateEngine()
    {
        var models = Path.Combine(AppContext.BaseDirectory, "ocr-models");
        var modelSet = RapidOcrModelSet.PPOCRv6Small with
        {
            DetModelPath = Path.Combine(models, "PP-OCRv6_det_small.onnx"),
            ClsModelPath = Path.Combine(models, "ch_ppocr_mobile_v2.0_cls_mobile.onnx"),
            RecModelPath = Path.Combine(models, "PP-OCRv6_rec_small.onnx"),
            KeysPath = Path.Combine(models, "ppocrv6_small_dict.txt")
        };
        var engine = new RapidOcr();
        using var sessionOptions = RapidOcr.GetDefaultSessionOptions();
        sessionOptions.EnableCpuMemArena = false;
        sessionOptions.EnableMemoryPattern = false;
        engine.InitModels(modelSet, sessionOptions);
        return engine;
    }

    public void Dispose()
    {
        foreach (var engine in _engines)
            if (engine.IsValueCreated) engine.Value.Dispose();
        _gate.Dispose();
    }
}
