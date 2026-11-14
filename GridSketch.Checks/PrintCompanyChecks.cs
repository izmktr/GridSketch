using System.IO;
using System.Text;
using GridSketch;

internal static class PrintCompanyChecks
{
    public static void Run(string directory)
    {
        var settings = PdfSettings.ForPreset("印刷会社用");
        if (!settings.PrintCompany || !settings.ConvertToCmyk || settings.BleedMm != 3 || settings.CropMarks || settings.JpegCompression)
            throw new Exception("Print company preset failed");
        var doc = new SceneDocument { PdfExport = settings };
        doc.Canvas.AutoSize = false; doc.Canvas.CanvasUnit = "mm"; doc.Canvas.Width = 148; doc.Canvas.Height = 105; doc.Canvas.Dpi = 96;
        doc.Canvas.Background = "#FFF6DC";
        doc.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Text, X = 24, Y = 24, Text = "印刷会社用 PDF", Color = "#000000", FontSize = 28 });
        doc.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Shape, X = 24, Y = 90, Width = 200, Height = 80, Thickness = 0, FillColor = "#FF8800" });
        doc.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Shape, X = 150, Y = 130, Width = 180, Height = 80, Thickness = 0, FillColor = "#800066FF" });
        doc.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Shape, X = -20, Y = 290, Width = 650, Height = 160, Thickness = 0, FillColor = "#008888" });
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 255, 0, 0, 128 }, 8);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var data = new MemoryStream(); png.Save(data);
        doc.Canvas.Children.Add(new SceneObject { Kind = ObjectKind.Image, ImageData = data.ToArray(), X = 360, Y = 90, Width = 120, Height = 60 });
        string file = Path.Combine(directory, "print-company.pdf");
        // No installed ICC is required to run the suite on another PC.
        if (settings.ProfileData is not null)
        {
            PdfExporter.Save(doc, file);
            var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(file));
            if (!pdf.Contains("/ICCBased") || !pdf.Contains("/OutputIntents") || !pdf.Contains("/TrimBox") || !pdf.Contains("/BleedBox") || pdf.Contains("/DeviceRGB") || pdf.Contains("/DCTDecode"))
                throw new Exception("CMYK PDF structure failed");
            var restored = SceneDocument.Restore(doc.Snapshot()); restored.Validate();
            if (!restored.PdfExport.ProfileData!.SequenceEqual(settings.ProfileData)) throw new Exception("ICC persistence failed");
            PdfExporter.Save(restored, Path.Combine(directory, "print-company-restored.pdf"));
            settings.CropMarks = true; PdfExporter.Save(doc, Path.Combine(directory, "print-company-marks.pdf"));
        }
        settings.ProfileData = null;
        try { PdfExporter.Save(doc, file + ".missing"); throw new Exception("Missing ICC silently exported"); } catch (InvalidOperationException) { }
        settings.ConvertToCmyk = false;
        PdfExporter.Save(doc, Path.Combine(directory, "print-company-rgb.pdf"));
        doc.Canvas.AutoSize = true;
        try { PdfExporter.Save(doc, file + ".auto"); throw new Exception("Auto canvas accepted for print"); } catch (InvalidOperationException) { }
        try { new PdfSettings { BleedMm = double.NaN }.Validate(); throw new Exception("Invalid bleed accepted"); } catch (InvalidDataException) { }
        try { PdfSettings.ValidateProfile(new byte[128]); throw new Exception("Invalid ICC accepted"); } catch (InvalidDataException) { }
        Console.WriteLine("PASS: Print-company PDF, ICC conversion/persistence, image alpha, trim/bleed, marks, missing profile and fixed-size checks");
    }
}
