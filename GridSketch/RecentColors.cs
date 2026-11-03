using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace GridSketch;

public sealed class RecentColors
{
    public static RecentColors Shared { get; } = new();
    private readonly string storagePath;
    private readonly List<Color> entries = [];
    public IReadOnlyList<Color> Entries => entries.AsReadOnly();

    public RecentColors(string? storagePath = null)
    {
        this.storagePath = storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridSketch", "recent-colors.json");
        try
        {
            if (!File.Exists(this.storagePath)) return;
            foreach (var value in JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(this.storagePath)) ?? [])
            {
                if (value is null || !ColorPicker.TryColor(value, out var color)) continue;
                color = Normalize(color);
                if (!entries.Contains(color)) entries.Add(color);
                if (entries.Count == 8) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { entries.Clear(); }
    }

    private static Color Normalize(Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public bool Remember(Color color)
    {
        color = Normalize(color);
        entries.Remove(color); entries.Insert(0, color);
        if (entries.Count > 8) entries.RemoveRange(8, entries.Count - 8);
        var temporary = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storagePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Select(ColorPicker.Format)));
            File.Move(temporary, storagePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
