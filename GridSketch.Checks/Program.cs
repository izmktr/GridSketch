using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GridSketch;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var temp = Path.Combine(Path.GetTempPath(), "GridSketch-check-" + Guid.NewGuid() + ".gridsketch");
        try
        {
            var pixels = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 100 * 50).SelectMany(p => p).ToArray();
            var source = BitmapSource.Create(100, 50, 96, 96, PixelFormats.Bgra32, null, pixels, 400);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using var memory = new MemoryStream(); encoder.Save(memory);
            var doc = new Document { Columns = 2, Rows = 2, CellSize = 64, Margin = 8 };
            doc.Cells.Add(new Cell { Column = 0, Row = 0, Image = memory.ToArray() });
            doc.Cells.Add(new Cell { Column = 1, Row = 1, Text = "あ", Font = "Yu Gothic" });
            doc.Save(temp);
            var loaded = Document.Load(temp);
            Check(loaded.Cells[0].Image!.SequenceEqual(memory.ToArray()) && loaded.Cells[1].Text == "あ", "Project round-trip including embedded image and Japanese text");
            loaded.Save(temp);
            Check(Document.Load(temp).Margin == 8, "Overwrite existing project");
            var surface = new GridSurface { Document = loaded, Selected = (0, 0) }; surface.Refresh();
            var bitmap = surface.Export(1);
            var buffer = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(buffer, bitmap.PixelWidth * 4, 0);
            bool Red(int x, int y) { int i = (y * bitmap.PixelWidth + x) * 4; return buffer[i + 2] > 240 && buffer[i] < 10; }
            bool White(int x, int y) { int i = (y * bitmap.PixelWidth + x) * 4; return buffer[i] == 255 && buffer[i + 1] == 255 && buffer[i + 2] == 255; }
            // 100x50 image shrinks to 48x24 within a 64px cell.
            int imagePad = (int)surface.PaddingSize;
            Check(Red(imagePad + 8, imagePad + 20) && Red(imagePad + 55, imagePad + 43) &&
                White(imagePad + 7, imagePad + 20) && White(imagePad + 56, imagePad + 43) &&
                White(imagePad + 32, imagePad + 19), "Image aspect ratio, central placement and margin");
            Check(White(5, 5), "Selection highlight excluded from export");
            Check(surface.Export(2).PixelWidth == bitmap.PixelWidth * 2, "Export resolution scaling");
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = new MemoryStream(); png.Save(output);
            Check(Document.DecodeImage(output.ToArray()).PixelWidth == bitmap.PixelWidth, "PNG encoding and decoding");
            loaded.Margin = 32; bool rejected = false; try { loaded.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid margin rejected");
            loaded.Margin = 8; loaded.Cells[1].Text = "ab"; rejected = false; try { loaded.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Multiple characters rejected");
            var maze = new Document { Columns = 2, Rows = 2, CellSize = 64 };
            var mazeSurface = new GridSurface { Document = maze }; mazeSurface.Refresh();
            var shared = new GridEdge(1, 0, true);
            double pad = mazeSurface.PaddingSize;
            var originalSize = new Size(mazeSurface.Width, mazeSurface.Height);
            var originalExport = mazeSurface.Export(1);
            maze.ToggleWall(shared); mazeSurface.Refresh();
            Check(mazeSurface.PaddingSize == pad && new Size(mazeSurface.Width, mazeSurface.Height) == originalSize &&
                mazeSurface.Export(1).PixelWidth == originalExport.PixelWidth && mazeSurface.Export(1).PixelHeight == originalExport.PixelHeight,
                "First wall preserves grid origin, canvas and PNG dimensions");
            maze.ToggleWall(shared); mazeSurface.Refresh();
            Check(mazeSurface.PaddingSize == pad && new Size(mazeSurface.Width, mazeSurface.Height) == originalSize,
                "Removing last wall preserves grid origin and canvas dimensions");
            Check(mazeSurface.EdgeAt(new Point(pad + 63, pad + 32), 1) == shared &&
                mazeSurface.EdgeAt(new Point(pad + 65, pad + 32), 1) == shared, "Shared edge hit from either cell");
            Check(mazeSurface.EdgeAt(new Point(pad + 32, pad + 32), 1) is null &&
                mazeSurface.EdgeAt(new Point(-50, -50), 1) is null, "Cell center and outside do not edit walls");
            Check(mazeSurface.EdgeAt(new Point(pad + 64 + 2, pad + 32), 3) == shared &&
                mazeSurface.EdgeAt(new Point(pad + 64 + 4, pad + 32), 3) is null, "Wall targeting accounts for zoom");
            Check(maze.ToggleWall(shared) && !maze.ToggleWall(shared) && maze.Walls.Count == 0, "Wall toggle removes shared edge");
            maze.ToggleWall(shared); maze.ToggleWall(new GridEdge(0, 0, false)); maze.ToggleWall(new GridEdge(2, 1, true));
            maze.Save(temp); maze = Document.Load(temp);
            Check(maze.Walls.SetEquals([shared, new GridEdge(0, 0, false), new GridEdge(2, 1, true)]) && maze.WallStroke.Width == 4, "Wall geometry and style survive save and load");
            mazeSurface.Document = maze; mazeSurface.Refresh();
            mazeSurface.HoveredEdge = new GridEdge(0, 1, false);
            var mazeBitmap = mazeSurface.Export(1);
            var mazePixels = new byte[mazeBitmap.PixelWidth * mazeBitmap.PixelHeight * 4];
            mazeBitmap.CopyPixels(mazePixels, mazeBitmap.PixelWidth * 4, 0);
            int mp = (int)mazeSurface.PaddingSize;
            bool Dark(int x, int y) { int i = (y * mazeBitmap.PixelWidth + x) * 4; return mazePixels[i + 2] < 60 && mazePixels[i + 1] < 70; }
            Check(Dark(mp + 63, mp + 32) && !Dark(mp + 63, mp + 96), "Only selected edge segment is thick in PNG");
            Check(Dark(mp + 32, mp - 2) && Dark(mp + 128 + 1, mp + 96), "Outer walls are not clipped");
            int hoverIndex = ((mp + 64) * mazeBitmap.PixelWidth + mp + 32) * 4;
            Check(mazePixels[hoverIndex + 2] > 100, "Wall hover preview excluded from PNG");
            maze.Walls.Add(new GridEdge(3, 0, true)); rejected = false;
            try { maze.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && !new GridEdge(2, 1, true).IsInside(1, 2), "Invalid walls and shrinking bounds rejected");
            File.WriteAllText(temp, "{\"Version\":1,\"Columns\":3,\"Rows\":4}");
            var legacy = Document.Load(temp);
            Check(legacy.Version == 2 && legacy.Walls.Count == 0 && legacy.Columns == 3, "Original projects migrate without walls");
            var historyPath = temp + ".history.json";
            try
            {
                var history = new RecentFiles(historyPath);
                Check(history.Entries.Count == 0, "Missing recent-file history starts empty");
                for (int i = 0; i < 12; i++) Check(history.Remember(temp + i), "Persist recent project " + i);
                var restartedHistory = new RecentFiles(historyPath);
                Check(restartedHistory.Entries.Count == 10 && restartedHistory.Entries[0] == temp + 11 &&
                    restartedHistory.Entries[9] == temp + 2, "Recent projects persist newest first with ten-item limit");
                restartedHistory.Remember((temp + 5).ToUpperInvariant());
                Check(restartedHistory.Entries.Count == 10 && string.Equals(restartedHistory.Entries[0], temp + 5, StringComparison.OrdinalIgnoreCase),
                    "Reopening a project promotes it without case-insensitive duplicates");
                restartedHistory.Remove(temp + 5);
                Check(new RecentFiles(historyPath).Entries.Count == 9, "Missing file removal persists");
                restartedHistory.Clear();
                Check(new RecentFiles(historyPath).Entries.Count == 0 && File.Exists(temp), "Clearing history preserves project files");
                File.WriteAllText(historyPath, "invalid json");
                var recovered = new RecentFiles(historyPath);
                Check(recovered.Entries.Count == 0 && recovered.Remember(temp), "Corrupt history recovers on next successful use");
                var unavailable = new RecentFiles(Path.Combine(temp, "history.json"));
                Check(!unavailable.Remember(temp) && unavailable.Entries.Count == 1, "History write failure retains in-session history without throwing");
            }
            finally { if (File.Exists(historyPath)) File.Delete(historyPath); }
            SceneChecks.Run(Path.GetDirectoryName(temp)!);
            // Construct only the test window; App.InitializeComponent queues a second StartupUri window.
            var app = new App(); var window = new MainWindow(); window.Show(); window.UpdateLayout();
            Check(window.IsVisible && window.ActualWidth > 0, "WPF window initialization and layout");
            WindowChecks.Run(window, Path.GetDirectoryName(temp)!);
            ColorPickerChecks.Run(window, Path.GetDirectoryName(temp)!);
            window.Close(); app.Shutdown();
            Console.WriteLine("All checks passed.");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }
}
