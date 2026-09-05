using System.Drawing;
using TarkovPriceOverlay.Configuration;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public sealed class InventoryScanCoordinator
{
    private readonly ScreenCaptureService _capture;
    private readonly IInventoryGridDetector _grids;
    private readonly IInventoryOccupancyDetector _occupancy;
    private readonly IOcrLayoutService _ocr;
    private readonly ItemCatalogService _catalog;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _gridConcurrency = new(2, 2);
    private readonly InventoryImageMatcher _imageMatcher;

    public InventoryScanCoordinator(
        ScreenCaptureService capture,
        IInventoryGridDetector grids,
        IInventoryOccupancyDetector occupancy,
        IOcrLayoutService ocr,
        ItemCatalogService catalog,
        AppSettings settings,
        InventoryImageMatcher? imageMatcher = null)
    {
        (_capture, _grids, _occupancy, _ocr, _catalog, _settings) =
            (capture, grids, occupancy, ocr, catalog, settings);
        _imageMatcher = imageMatcher ?? new InventoryImageMatcher();
    }

    public async Task<IReadOnlyList<DetectedInventoryItem>> ScanAsync(
        Action? onCaptured = null, CancellationToken ct = default)
    {
        using var frame = _capture.CaptureCurrentScreen();
        onCaptured?.Invoke();
        return await ScanFrameAsync(frame, ct);
    }

    public async Task<IReadOnlyList<DetectedInventoryItem>> ScanFrameAsync(
        CapturedRegion frame, CancellationToken ct = default)
    {
        var grids = _grids.Detect(frame.Image);
        var occupiedByGrid = _occupancy.DetectMany(frame.Image, grids)
            .ToDictionary(pair => pair.Key,
                pair => (IReadOnlySet<(int Column, int Row)>)(pair.Key.Kind == "随身区域"
                    ? (from row in Enumerable.Range(0, pair.Key.VisibleRows)
                       from column in Enumerable.Range(0, pair.Key.VisibleColumns)
                       select (column, row)).ToHashSet()
                    : pair.Value.Select(cell =>
                        (cell.CellBounds.X, cell.CellBounds.Y)).ToHashSet()));
        var mode = _settings.RecognitionMode;
        IReadOnlyList<DetectedInventoryItem> matches;
        if (mode.Equals("Fast", StringComparison.OrdinalIgnoreCase))
        {
            var collected = new List<DetectedInventoryItem>();
            foreach (var grid in grids)
                collected.AddRange(await ScanGridAsync(frame, grid,
                    useAllLanguageOrders: false, scale: 1,
                    neuralOnly: true, ct,
                    occupiedByGrid[grid]));
            matches = collected;
        }
        else if (mode.Equals("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            var tasks = grids.Select(async grid =>
            {
                await _gridConcurrency.WaitAsync(ct);
                try
                {
                    return await ScanQualityGridAsync(
                        frame, grid, precise: true, occupiedByGrid[grid], ct);
                }
                finally
                {
                    _gridConcurrency.Release();
                }
            });
            matches = (await Task.WhenAll(tasks)).SelectMany(items => items).ToList();
        }
        else
        {
            var collected = new List<DetectedInventoryItem>();
            foreach (var grid in grids)
                collected.AddRange(await ScanQualityGridAsync(
                    frame, grid, precise: false, occupiedByGrid[grid], ct));
            matches = collected;
        }

        var strongestPerAnchor = matches
            .GroupBy(x => (x.GridKind, X: x.ScreenBounds.X, Y: x.ScreenBounds.Y))
            .Select(group => group.OrderByDescending(x => x.Confidence).First())
            .ToList();
        return RemoveOverlappingDuplicates(strongestPerAnchor);
    }

    private async Task<IReadOnlyList<DetectedInventoryItem>> ScanQualityGridAsync(
        CapturedRegion frame, InventoryGridRegion grid, bool precise,
        IReadOnlySet<(int Column, int Row)> occupied, CancellationToken ct)
    {
        if (occupied.Count == 0) return [];
        var baseline = await ScanGridAsync(frame, grid,
            useAllLanguageOrders: false, scale: 1,
            neuralOnly: true, ct, occupied);
        var fallback = await ScanUnmatchedCellsBatchAsync(
            frame, grid, occupied, baseline, useHybridOcr: false, ct);
        var combined = baseline.Concat(fallback).ToList();
        var unresolved = occupied.Count(cell =>
            !IsCellCovered(frame, grid, cell.Column, cell.Row, combined));
        if (unresolved >= 2 &&
            grid.Kind is "弹挂" or "口袋" or "背包" or "安全箱" or "容器")
        {
            var enhanced = await ScanGridAsync(frame, grid,
                useAllLanguageOrders: false, scale: _settings.OcrImageScale,
                neuralOnly: true, ct, occupied);
            combined.AddRange(enhanced);
        }
        if (precise)
        {
            var confirmed = await ScanUnmatchedCellsBatchAsync(
                frame, grid, occupied, combined, useHybridOcr: true, ct);
            combined.AddRange(confirmed);
        }
        return combined;
    }

    private async Task<IReadOnlyList<DetectedInventoryItem>> ScanGridAsync(
        CapturedRegion frame, InventoryGridRegion grid, bool useAllLanguageOrders,
        int scale, bool neuralOnly, CancellationToken ct,
        IReadOnlySet<(int Column, int Row)>? occupiedCells = null)
    {
        var broadCarriedArea = grid.Kind == "随身区域";
        var occupied = occupiedCells ?? DetectOccupiedCells(frame.Image, grid);
        if (occupied.Count == 0) return [];

        Bitmap cropImage;
        lock (frame.Image)
            cropImage = frame.Image.Clone(grid.Bounds, frame.Image.PixelFormat);
        using var crop = cropImage;
        using var enhanced = OcrImagePreprocessor.CreatePrimaryVariant(crop, scale);
        var scaleX = enhanced.Width / (double)crop.Width;
        var scaleY = enhanced.Height / (double)crop.Height;
        var words = neuralOnly && _ocr is HybridOcrService hybrid
            ? await hybrid.RecognizeNeuralBlocksAsync(enhanced, ct)
            : _ocr is IConfigurableOcrLayoutService configurable
                ? await configurable.RecognizeBlocksAsync(enhanced, useAllLanguageOrders, ct)
                : await _ocr.RecognizeBlocksAsync(enhanced, ct);

        var candidates = ExpandTextBlocks(words)
        .Where(word => word.Bounds.Width <= grid.CellWidth * scaleX * 1.35 ||
                       !word.Text.Contains(' '))
        .Select(word =>
        {
            var localX = Math.Max(0, word.Bounds.Right - Math.Max(2d, scaleX * 4d)) / scaleX;
            var localY = (word.Bounds.Top + word.Bounds.Height / 2d) / scaleY;
            var column = Math.Clamp((int)(localX / grid.CellWidth), 0, grid.VisibleColumns - 1);
            var row = Math.Clamp((int)(localY / grid.CellHeight), 0, grid.VisibleRows - 1);
            return (Word: word, Column: column, Row: row, LocalY: localY);
        })
        .Where(x => HasNearbyOccupiedCell(occupied, x.Column, x.Row))
        .Where(x => broadCarriedArea ||
            x.LocalY - x.Row * grid.CellHeight < grid.CellHeight * 0.42)
        .GroupBy(x => (x.Column, x.Row));

        var matches = new List<DetectedInventoryItem>();
        foreach (var cell in candidates)
        {
            var cellWords = cell.ToList();
            var text = string.Join(" ", cellWords.OrderBy(x => x.Word.Bounds.Left)
                .Select(x => x.Word.Text));
            if (text.Count(char.IsLetterOrDigit) < 2) continue;
            var normalizedText = new string(text.ToUpperInvariant()
                .Where(char.IsLetterOrDigit).ToArray());
            var containsCjk = text.Any(character => character is >= '\u3400' and <= '\u9FFF');
            if (!containsCjk && normalizedText.Length <= 2 &&
                normalizedText is not ("TT" or "PP" or "F1") &&
                !_catalog.FindCandidates(text).Any(candidate => candidate.Score >= 0.99))
                continue;
            var (item, score) = _catalog.FindBest(text);
            // A detector block can bridge neighboring slots. Prefer an atomic
            // token when it independently gives a stronger catalog match.
            var atomic = cellWords
                .Where(candidate => !candidate.Word.Text.Contains(' '))
                .Select(candidate =>
                {
                    var match = _catalog.FindBest(candidate.Word.Text);
                    return (candidate.Word.Text, match.Item, match.Score);
                })
                .Where(match => match.Item is not null)
                .OrderByDescending(match => match.Score)
                .FirstOrDefault();
            if (atomic.Item is not null && atomic.Score >= score)
                (text, item, score) = (atomic.Text, atomic.Item, atomic.Score);
            var ocrConfidence = cellWords.Average(x => x.Word.Confidence);
            var combined = score * 0.8 + ocrConfidence * 0.2;
            var minimumCatalogScore = containsCjk ? 0.76 : 0.84;
            var visualResolved = false;
            if (item is null || score < minimumCatalogScore)
            {
                var visualCandidates = _catalog.FindCandidates(text);
                if (visualCandidates.Count > 0)
                {
                    var visual = await _imageMatcher.MatchAsync(
                        candidate => CropItemIcon(frame, grid, cell.Key.Column, cell.Key.Row,
                            candidate.Width, candidate.Height),
                        visualCandidates, ct);
                    if (visual.Item is not null)
                    {
                        item = visual.Item;
                        visualResolved = true;
                        score = Math.Max(score, visual.Score);
                        combined = Math.Max(combined,
                            visual.Score * 0.8 + ocrConfidence * 0.2);
                    }
                }
            }
            if (item is null || (!visualResolved && score < minimumCatalogScore) ||
                visualResolved && score < 0.62 || combined < 0.78) continue;

            var anchor = cellWords.Select(candidate =>
                {
                    var (anchorItem, anchorScore) = _catalog.FindBest(candidate.Word.Text);
                    return (Candidate: candidate, Item: anchorItem, Score: anchorScore);
                })
                .Where(candidate => candidate.Item?.Id.Equals(
                    item.Id, StringComparison.OrdinalIgnoreCase) == true)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Candidate.Word.Confidence)
                .Select(candidate => candidate.Candidate)
                .FirstOrDefault();
            var anchorColumn = anchor.Word is null
                ? cell.Key.Column
                : EstimateAnchorColumn(anchor.Word, item, scaleX, grid);
            var anchorRow = anchor.Word is null ? cell.Key.Row : anchor.Row;
            Rectangle bounds;
            lock (frame.Image)
                bounds = CreateItemBounds(frame, grid, anchorColumn, anchorRow,
                    item.Width, item.Height);
            matches.Add(new(item, bounds, combined, grid.Kind, text));
        }
        return matches;
    }

    private HashSet<(int Column, int Row)> DetectOccupiedCells(
        Bitmap image, InventoryGridRegion grid)
        => grid.Kind == "随身区域"
            ? (from row in Enumerable.Range(0, grid.VisibleRows)
               from column in Enumerable.Range(0, grid.VisibleColumns)
               select (column, row)).ToHashSet()
            : _occupancy.Detect(image, grid)
                .Select(cell => (cell.CellBounds.X, cell.CellBounds.Y)).ToHashSet();

    private async Task<IReadOnlyList<DetectedInventoryItem>> ScanUnmatchedCellsBatchAsync(
        CapturedRegion frame,
        InventoryGridRegion grid,
        IReadOnlySet<(int Column, int Row)> occupied,
        IReadOnlyList<DetectedInventoryItem> existing,
        bool useHybridOcr,
        CancellationToken ct)
    {
        var missing = occupied
            .Where(cell => !IsCellCovered(frame, grid, cell.Column, cell.Row, existing))
            .OrderBy(cell => cell.Row).ThenBy(cell => cell.Column)
            .ToList();
        if (missing.Count == 0) return [];

        var scale = Math.Max(4, _settings.OcrImageScale);
        var sourceBandHeight = Math.Max(18, (int)Math.Round(grid.CellHeight * 0.48));
        var tileWidth = Math.Max(8, (int)Math.Ceiling(grid.CellWidth * scale));
        var tileHeight = Math.Max(8, sourceBandHeight * scale);
        // Wide gutters prevent the detector from merging names from adjacent
        // slots (for example Surv12 + 绿色信号) into one OCR block.
        const int padding = 48;
        var strideWidth = tileWidth + padding * 2;
        var strideHeight = tileHeight + padding * 2;
        // Keep neural-detector inputs below ~1400 px on either side. Wider
        // montages make ONNX Runtime permanently grow its native arena.
        const int maximumTilesPerBatch = 12;
        const int tilesPerRow = 3;
        var results = new List<DetectedInventoryItem>();
        foreach (var batch in missing.Chunk(maximumTilesPerBatch))
        {
            var batchRows = (int)Math.Ceiling(batch.Length / (double)tilesPerRow);
            using var montage = new Bitmap(
                strideWidth * Math.Min(tilesPerRow, batch.Length),
                strideHeight * batchRows,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(montage))
            {
                graphics.Clear(Color.Black);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                for (var index = 0; index < batch.Length; index++)
                {
                    var (column, row) = batch[index];
                    var left = grid.Bounds.Left + (int)Math.Round(column * grid.CellWidth);
                    var top = grid.Bounds.Top + (int)Math.Round(row * grid.CellHeight);
                    var right = Math.Min(grid.Bounds.Right,
                        grid.Bounds.Left + (int)Math.Round((column + 1) * grid.CellWidth));
                    var bottom = Math.Min(grid.Bounds.Bottom, top + sourceBandHeight);
                    if (right <= left || bottom <= top) continue;
                    var tileColumn = index % tilesPerRow;
                    var tileRow = index / tilesPerRow;
                    lock (frame.Image)
                        graphics.DrawImage(frame.Image,
                            new Rectangle(tileColumn * strideWidth + padding,
                                tileRow * strideHeight + padding, tileWidth, tileHeight),
                            Rectangle.FromLTRB(left, top, right, bottom), GraphicsUnit.Pixel);
                }
            }

            var blocks = useHybridOcr && _ocr is IConfigurableOcrLayoutService configurable
                ? await configurable.RecognizeBlocksAsync(montage, true, ct)
                : _ocr is HybridOcrService hybrid
                    ? await hybrid.RecognizeNeuralBlocksAsync(montage, ct)
                    : await _ocr.RecognizeBlocksAsync(montage, ct);
            var cells = blocks.Where(block => block.Text.Any(char.IsLetterOrDigit))
                .GroupBy(block => Math.Clamp(
                    ((block.Bounds.Top + block.Bounds.Height / 2) / strideHeight) * tilesPerRow +
                    ((block.Bounds.Left + block.Bounds.Width / 2) / strideWidth),
                    0, batch.Length - 1));
            foreach (var cell in cells)
            {
                var (column, row) = batch[cell.Key];
                var text = string.Join(" ", cell.OrderBy(block => block.Bounds.Left)
                    .Select(block => block.Text));
                if (text.Count(char.IsLetterOrDigit) < 2) continue;
                var (item, score) = _catalog.FindBest(text);
                var confidence = score * 0.8 + cell.Average(block => block.Confidence) * 0.2;
                var visualResolved = false;
                if (item is null || score < 0.84)
                {
                    var visualCandidates = _catalog.FindCandidates(text);
                    if (visualCandidates.Count > 0)
                    {
                        var visual = await _imageMatcher.MatchAsync(
                            candidate => CropItemIcon(frame, grid,
                                column + candidate.Width - 1, row,
                                candidate.Width, candidate.Height),
                            visualCandidates, ct);
                        if (visual.Item is not null)
                        {
                            item = visual.Item;
                            visualResolved = true;
                            score = Math.Max(score, visual.Score);
                            confidence = Math.Max(confidence,
                                visual.Score * 0.8 + cell.Average(block => block.Confidence) * 0.2);
                        }
                    }
                }
                if (item is null || (!visualResolved && score < 0.76) ||
                    visualResolved && score < 0.62) continue;
                if (confidence < 0.78) continue;
                var rightColumn = Math.Min(grid.VisibleColumns - 1,
                    column + item.Width - 1);
                Rectangle bounds;
                lock (frame.Image)
                    bounds = CreateItemBounds(frame, grid, rightColumn, row,
                        item.Width, item.Height);
                results.Add(new(item, bounds, confidence, grid.Kind, text));
            }
        }
        return results;
    }

    private static Bitmap? CropItemIcon(
        CapturedRegion frame, InventoryGridRegion grid, int rightColumn, int row,
        int itemWidth, int itemHeight)
    {
        var bounds = CreateItemBounds(frame, grid, rightColumn, row, itemWidth, itemHeight);
        var crop = bounds;
        crop.Offset(-frame.Origin.X, -frame.Origin.Y);
        var trimX = Math.Clamp((int)Math.Round(crop.Width * 0.06), 2, 6);
        var trimTop = Math.Clamp((int)Math.Round(crop.Height * 0.18), 5, 16);
        var trimBottom = Math.Clamp((int)Math.Round(crop.Height * 0.04), 2, 5);
        crop = Rectangle.FromLTRB(crop.Left + trimX, crop.Top + trimTop,
            crop.Right - trimX, crop.Bottom - trimBottom);
        crop.Intersect(new Rectangle(Point.Empty, frame.Image.Size));
        if (crop.Width < 12 || crop.Height < 12) return null;
        lock (frame.Image)
            return frame.Image.Clone(crop, frame.Image.PixelFormat);
    }

    private static bool IsCellCovered(
        CapturedRegion frame,
        InventoryGridRegion grid,
        int column,
        int row,
        IReadOnlyList<DetectedInventoryItem> existing)
    {
        var left = grid.Bounds.Left + (int)Math.Round(column * grid.CellWidth);
        var top = grid.Bounds.Top + (int)Math.Round(row * grid.CellHeight);
        var right = grid.Bounds.Left + (int)Math.Round((column + 1) * grid.CellWidth);
        var bottom = grid.Bounds.Top + (int)Math.Round((row + 1) * grid.CellHeight);
        var center = new Point(frame.Origin.X + (left + right) / 2,
            frame.Origin.Y + (top + bottom) / 2);
        return existing.Any(item => item.ScreenBounds.Contains(center));
    }

    private static Rectangle CreateItemBounds(
        CapturedRegion frame,
        InventoryGridRegion grid,
        int rightColumn,
        int row,
        int itemWidth,
        int itemHeight)
    {
        itemWidth = Math.Max(1, itemWidth);
        itemHeight = Math.Max(1, itemHeight);
        var column = rightColumn - itemWidth + 1;
        if (grid.Kind is "已装备胸挂" or "已装备背包")
            (column, row, itemWidth, itemHeight) = (0, 0, 2, 2);
        else if (grid.Kind is "口袋" or "特殊装备栏")
            (column, itemWidth, itemHeight) = (rightColumn, 1, 1);
        else if (grid.Kind == "随身区域")
        {
            // Equipped rigs and backpacks are rendered inside fixed 2x2 slots,
            // regardless of their much larger stash footprint.
            if (rightColumn <= 1 && row <= 1)
                (column, row, itemWidth, itemHeight) = (0, 0, 2, 2);
            else if (rightColumn <= 2 && row is 6 or 7)
                (column, row, itemWidth, itemHeight) = (0, 6, 2, 2);
            // Pockets and special equipment slots are each rendered as one
            // visual slot even when the underlying item occupies more cells.
            else if (row == 4)
                (itemWidth, itemHeight) = (1, 1);
        }
        var normalFits = column >= 0 && column + itemWidth <= grid.VisibleColumns &&
                         row + itemHeight <= grid.VisibleRows;
        var rotatedColumn = rightColumn - itemHeight + 1;
        var rotatedFits = rotatedColumn >= 0 &&
                          rotatedColumn + itemHeight <= grid.VisibleColumns &&
                          row + itemWidth <= grid.VisibleRows;
        var chooseRotated = !normalFits && rotatedFits;
        if (normalFits && rotatedFits && itemWidth != itemHeight)
        {
            var normalInternal = InternalBoundaryEvidence(frame.Image, grid, column, row,
                itemWidth, itemHeight);
            var rotatedInternal = InternalBoundaryEvidence(frame.Image, grid, rotatedColumn, row,
                itemHeight, itemWidth);
            if (Math.Abs(normalInternal - rotatedInternal) >= 1.5)
                chooseRotated = rotatedInternal < normalInternal;
            else
            {
                var normalScore = BorderEvidence(frame.Image, grid, column, row,
                    itemWidth, itemHeight);
                var rotatedScore = BorderEvidence(frame.Image, grid, rotatedColumn, row,
                    itemHeight, itemWidth);
                chooseRotated = rotatedScore > normalScore * 1.08;
            }
        }
        if (chooseRotated)
        {
            (itemWidth, itemHeight) = (itemHeight, itemWidth);
            column = rotatedColumn;
        }
        column = Math.Max(0, column);

        var cellsWide = Math.Min(itemWidth, grid.VisibleColumns - column);
        var cellsHigh = Math.Min(itemHeight, grid.VisibleRows - row);
        var localLeft = grid.Bounds.Left + (int)Math.Round(column * grid.CellWidth);
        var localTop = grid.Bounds.Top + (int)Math.Round(row * grid.CellHeight);
        var localRight = Math.Min(grid.Bounds.Right,
            grid.Bounds.Left + (int)Math.Round((column + cellsWide) * grid.CellWidth));
        var localBottom = Math.Min(grid.Bounds.Bottom,
            grid.Bounds.Top + (int)Math.Round((row + cellsHigh) * grid.CellHeight));
        return Rectangle.FromLTRB(
            frame.Origin.X + localLeft,
            frame.Origin.Y + localTop,
            frame.Origin.X + Math.Max(localLeft + 1, localRight),
            frame.Origin.Y + Math.Max(localTop + 1, localBottom));
    }

    private static double BorderEvidence(
        Bitmap image,
        InventoryGridRegion grid,
        int column,
        int row,
        int width,
        int height)
    {
        var left = (int)Math.Round(grid.Bounds.Left + column * grid.CellWidth);
        var top = (int)Math.Round(grid.Bounds.Top + row * grid.CellHeight);
        var right = (int)Math.Round(grid.Bounds.Left + (column + width) * grid.CellWidth);
        var bottom = (int)Math.Round(grid.Bounds.Top + (row + height) * grid.CellHeight);
        return (VerticalBorderScore(image, left, top, bottom) +
                VerticalBorderScore(image, right, top, bottom) +
                HorizontalBorderScore(image, top, left, right) +
                HorizontalBorderScore(image, bottom, left, right)) / 4d;
    }

    private static double InternalBoundaryEvidence(
        Bitmap image,
        InventoryGridRegion grid,
        int column,
        int row,
        int width,
        int height)
    {
        var left = (int)Math.Round(grid.Bounds.Left + column * grid.CellWidth);
        var top = (int)Math.Round(grid.Bounds.Top + row * grid.CellHeight);
        var right = (int)Math.Round(grid.Bounds.Left + (column + width) * grid.CellWidth);
        var bottom = (int)Math.Round(grid.Bounds.Top + (row + height) * grid.CellHeight);
        var scores = new List<double>();
        for (var offset = 1; offset < width; offset++)
        {
            var x = (int)Math.Round(grid.Bounds.Left + (column + offset) * grid.CellWidth);
            scores.Add(VerticalBorderScore(image, x, top, bottom));
        }
        for (var offset = 1; offset < height; offset++)
        {
            var y = (int)Math.Round(grid.Bounds.Top + (row + offset) * grid.CellHeight);
            scores.Add(HorizontalBorderScore(image, y, left, right));
        }
        return scores.Count == 0 ? double.MaxValue : scores.Average();
    }

    private static double VerticalBorderScore(Bitmap image, int x, int top, int bottom)
    {
        x = Math.Clamp(x, 2, image.Width - 3);
        top = Math.Clamp(top + 4, 0, image.Height - 1);
        bottom = Math.Clamp(bottom - 4, top + 1, image.Height);
        double total = 0;
        var count = 0;
        for (var y = top; y < bottom; y += 3)
        {
            total += Math.Abs(Luma(image.GetPixel(x - 2, y)) -
                              Luma(image.GetPixel(x + 2, y)));
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double HorizontalBorderScore(Bitmap image, int y, int left, int right)
    {
        y = Math.Clamp(y, 2, image.Height - 3);
        left = Math.Clamp(left + 4, 0, image.Width - 1);
        right = Math.Clamp(right - 4, left + 1, image.Width);
        double total = 0;
        var count = 0;
        for (var x = left; x < right; x += 3)
        {
            total += Math.Abs(Luma(image.GetPixel(x, y - 2)) -
                              Luma(image.GetPixel(x, y + 2)));
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double Luma(Color color) =>
        color.R * 0.299 + color.G * 0.587 + color.B * 0.114;

    private static IReadOnlyList<DetectedInventoryItem> RemoveOverlappingDuplicates(
        IReadOnlyList<DetectedInventoryItem> items)
    {
        var kept = new List<DetectedInventoryItem>();
        foreach (var candidate in items.OrderByDescending(item => item.Confidence))
        {
            var duplicate = kept.Any(existing =>
                existing.GridKind == candidate.GridKind &&
                existing.Item.Id.Equals(candidate.Item.Id, StringComparison.OrdinalIgnoreCase) &&
                OverlapRatio(existing.ScreenBounds, candidate.ScreenBounds) >= 0.5);
            if (!duplicate) kept.Add(candidate);
        }
        return kept;
    }

    private static double OverlapRatio(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        if (intersection.IsEmpty) return 0;
        var intersectionArea = (double)intersection.Width * intersection.Height;
        var smallerArea = Math.Min(
            (double)first.Width * first.Height,
            (double)second.Width * second.Height);
        return smallerArea <= 0 ? 0 : intersectionArea / smallerArea;
    }

    private static bool HasNearbyOccupiedCell(
        IReadOnlySet<(int Column, int Row)> occupied,
        int column,
        int row)
    {
        for (var y = row - 1; y <= row + 1; y++)
        for (var x = column - 1; x <= column + 1; x++)
            if (occupied.Contains((x, y))) return true;
        return false;
    }

    private static int EstimateAnchorColumn(
        OcrTextBlock block,
        ItemPrice item,
        double scaleX,
        InventoryGridRegion grid)
    {
        var normalizedBlock = NormalizeText(block.Text);
        var located = new[] { item.Name, item.ShortName }
            .Select(NormalizeText)
            .Where(name => name.Length > 0)
            .Select(name => (Name: name,
                Index: normalizedBlock.IndexOf(name, StringComparison.OrdinalIgnoreCase)))
            .Where(match => match.Index >= 0)
            .OrderByDescending(match => match.Name.Length)
            .FirstOrDefault();
        var right = (double)block.Bounds.Right;
        if (located.Name is not null && normalizedBlock.Length > 0)
        {
            var rightFraction = (located.Index + located.Name.Length) /
                                (double)normalizedBlock.Length;
            right = block.Bounds.Left + block.Bounds.Width * rightFraction;
        }
        var localRight = Math.Max(0, right - Math.Max(2d, scaleX * 4d)) / scaleX;
        return Math.Clamp((int)(localRight / grid.CellWidth), 0, grid.VisibleColumns - 1);
    }

    private static string NormalizeText(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static IEnumerable<OcrTextBlock> ExpandTextBlocks(
        IReadOnlyList<OcrTextBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            var tokens = block.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries |
                                               StringSplitOptions.TrimEntries);
            if (tokens.Length <= 1 || block.Bounds.Width < tokens.Length * 12) continue;
            var searchStart = 0;
            foreach (var token in tokens)
            {
                var index = block.Text.IndexOf(token, searchStart,
                    StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                var leftFraction = index / (double)Math.Max(1, block.Text.Length);
                var rightFraction = (index + token.Length) /
                                    (double)Math.Max(1, block.Text.Length);
                var left = block.Bounds.Left + (int)Math.Round(block.Bounds.Width * leftFraction);
                var right = block.Bounds.Left + (int)Math.Round(block.Bounds.Width * rightFraction);
                yield return new OcrTextBlock(token,
                    Rectangle.FromLTRB(left, block.Bounds.Top,
                        Math.Max(left + 1, right), block.Bounds.Bottom),
                    block.Confidence);
                searchStart = index + token.Length;
            }
        }
    }

}
