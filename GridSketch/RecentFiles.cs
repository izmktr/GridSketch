using System.IO;
using System.Text.Json;

namespace GridSketch;

public sealed class RecentFiles
{
    private readonly string storagePath;
    private readonly List<string> entries = [];
    public IReadOnlyList<string> Entries => entries.AsReadOnly();

    public RecentFiles(string? storagePath = null)
    {
        this.storagePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridSketch", "recent-files.json");
        try
        {
            if (!File.Exists(this.storagePath)) return;
            var saved = JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(this.storagePath));
            foreach (var item in saved ?? [])
            {
                if (string.IsNullOrWhiteSpace(item) || !Path.IsPathFullyQualified(item)) continue;
                string fullPath;
                try { fullPath = Path.GetFullPath(item); }
                catch (ArgumentException) { continue; }
                if (!entries.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) entries.Add(fullPath);
                if (entries.Count == 10) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            entries.Clear();
        }
    }

    public bool Remember(string path)
    {
        var fullPath = Path.GetFullPath(path);
        entries.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, fullPath);
        if (entries.Count > 10) entries.RemoveRange(10, entries.Count - 10);
        return Persist();
    }

    public bool Remove(string path)
    {
        entries.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        return Persist();
    }

    public bool Clear()
    {
        entries.Clear();
        return Persist();
    }

    private bool Persist()
    {
        var temporary = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storagePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
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
