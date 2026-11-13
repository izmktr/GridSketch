using System.IO;
using System.Windows.Media.Imaging;

namespace GridSketch;

public sealed record BatchExportItem(string Name, SceneDocument Document, string? PresetName = null);
public sealed record BatchExportPlan(IReadOnlyList<BatchExportItem> Jobs, IReadOnlyList<string> Excluded);
public sealed record BatchExportResult(string Name, string? Path, string? Error);

public static class BatchExporter
{
    public static BatchExportPlan Plan(IEnumerable<BatchExportItem> items, IEnumerable<string> presetNames)
    {
        var jobs = new List<BatchExportItem>(); var excluded = new List<string>();
        var names = presetNames.Distinct(StringComparer.Ordinal).ToList();
        foreach (var item in items)
            foreach (var name in names)
            {
                int count = item.Document.VisibilityPresets.Count(p => p.Name == name);
                if (count == 1) jobs.Add(item with { PresetName = name });
                else excluded.Add($"{item.Name}：{name}（{(count == 0 ? "なし" : "同名が複数")}・除外）");
            }
        return new(jobs, excluded);
    }

    public static IReadOnlyList<BatchExportResult> Export(IEnumerable<BatchExportItem> items, string folder, string format)
    {
        if (format is not ("PNG" or "PDF")) throw new ArgumentException("出力形式を選択してください。");
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("出力先フォルダを選択してください。");
        folder = System.IO.Path.GetFullPath(folder);
        var results = new List<BatchExportResult>();
        foreach (var item in items)
        {
            string outputName = item.PresetName is null ? item.Name : item.Name + "_" + item.PresetName;
            string? temporary = null;
            try
            {
                var invalid = System.IO.Path.GetInvalidFileNameChars();
                var stem = new string(outputName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
                if (string.IsNullOrWhiteSpace(stem)) stem = "無題";
                if (stem.Length > 120) stem = stem[..120];
                var reserved = stem.Split('.')[0].ToUpperInvariant();
                if (reserved is "CON" or "PRN" or "AUX" or "NUL" ||
                    Enumerable.Range(1, 9).Any(n => reserved == "COM" + n || reserved == "LPT" + n)) stem = "_" + stem;
                string extension = "." + format.ToLowerInvariant();
                string target = System.IO.Path.Combine(folder, stem + extension);
                int suffix = 2;
                while (File.Exists(target) || Directory.Exists(target)) target = System.IO.Path.Combine(folder, stem + " (" + suffix++ + ")" + extension);
                temporary = System.IO.Path.Combine(folder, ".gridsketch-export-" + Guid.NewGuid().ToString("N") + ".tmp");
                var copy = SceneDocument.Restore(item.Document.Snapshot());
                if (item.PresetName is { } presetName)
                {
                    var presets = copy.VisibilityPresets.Where(p => p.Name == presetName).ToList();
                    if (presets.Count != 1) throw new InvalidDataException("表示プリセットを一意に特定できません。");
                    copy.ApplyVisibility(presets[0]);
                }
                copy.Validate();
                if (format == "PDF") PdfExporter.Save(copy, temporary);
                else
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(copy).Export(1)));
                    using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write);
                    encoder.Save(stream);
                }
                File.Move(temporary, target); // Never overwrite an existing export.
                results.Add(new(outputName, target, null));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or OverflowException or FormatException)
            {
                results.Add(new(outputName, null, ex.Message));
            }
            finally
            {
                if (temporary is not null && File.Exists(temporary))
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        return results;
    }
}
