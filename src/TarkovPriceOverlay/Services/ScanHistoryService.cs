using System.Text.Json;
using TarkovPriceOverlay.Models;

namespace TarkovPriceOverlay.Services;

public sealed class ScanHistoryService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LootPilot", "scan-history.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyList<ScanHistoryEntry>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<ScanHistoryEntry>>(stream, JsonOptions, ct) ?? [];
    }

    public async Task AddAsync(ScanHistoryEntry entry, CancellationToken ct = default)
    {
        var entries = (await LoadAsync(ct)).ToList();
        entries.Insert(0, entry);
        if (entries.Count > 50) entries.RemoveRange(50, entries.Count - 50);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, ct);
        File.Move(temp, _path, true);
    }
}
