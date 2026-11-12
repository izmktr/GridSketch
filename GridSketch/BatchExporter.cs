using System.IO;
using System.Windows.Media.Imaging;

namespace GridSketch;

public sealed record BatchExportItem(string Name, SceneDocument Document);
public sealed record BatchExportResult(string Name, string? Path, string? Error);

public static class BatchExporter
{
    public static IReadOnlyList<BatchExportResult> Export(IEnumerable<BatchExportItem> items, string folder, string format)
    {
        if (format is not ("PNG" or "PDF")) throw new ArgumentException("出力形式を選択してください。");
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("出力先フォルダを選択してください。");
        folder = System.IO.Path.GetFullPath(folder);
        var results = new List<BatchExportResult>();
        foreach (var item in items)
        {
            string? temporary = null;
            try
            {
                var invalid = System.IO.Path.GetInvalidFileNameChars();
                var stem = new string(item.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
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
                var copy = SceneDocument.Restore(item.Document.Snapshot()); copy.Validate();
                if (format == "PDF") PdfExporter.Save(copy, temporary);
                else
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(new SceneRenderer(copy).Export(1)));
                    using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write);
                    encoder.Save(stream);
                }
                File.Move(temporary, target); // Never overwrite an existing export.
                results.Add(new(item.Name, target, null));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or OverflowException or FormatException)
            {
                results.Add(new(item.Name, null, ex.Message));
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
