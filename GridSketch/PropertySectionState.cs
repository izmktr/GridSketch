using System.IO;
using System.Text.Json;

namespace GridSketch;

public sealed class PropertySectionState
{
    public static PropertySectionState Shared { get; } = new();
    private readonly string storagePath;
    private readonly Dictionary<string, bool> values;

    public PropertySectionState(string? path = null)
    {
        storagePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridSketch", "property-sections.json");
        try { values = File.Exists(storagePath) ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(storagePath)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { values = []; }
    }

    public bool IsOpen(string key, bool initial) => values.GetValueOrDefault(key, initial);

    public void Set(string key, bool open)
    {
        values[key] = open;
        var temporary = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storagePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(values));
            File.Move(temporary, storagePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
