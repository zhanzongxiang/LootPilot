using System.Drawing;

namespace TarkovPriceOverlay.Models;

public sealed record OcrTextBlock(string Text, Rectangle Bounds, double Confidence);

public sealed record DetectedInventoryItem(
    ItemPrice Item,
    Rectangle ScreenBounds,
    double Confidence,
    string GridKind,
    string RawText);
