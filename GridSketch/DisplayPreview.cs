using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridSketch;

public enum ColorVisionPreview { None, Protan, Deutan, Tritan }

public static class DisplayPreview
{
    // Machado, Oliveira & Fernandes (2009), severity 1.0, applied in linear sRGB.
    // https://www.inf.ufrgs.br/~oliveira/pubs_files/CVD_Simulation/CVD_Simulation.html
    private static readonly double[][] matrices = [
        [1,0,0,0,1,0,0,0,1],
        [0.152286,1.052583,-0.204868,0.114503,0.786281,0.099216,-0.003882,-0.048116,1.051998],
        [0.367322,0.860646,-0.227968,0.280085,0.672501,0.047413,-0.011820,0.042940,0.968881],
        [1.255528,-0.076749,-0.178779,-0.078411,0.930809,0.147602,0.004733,0.691367,0.303900]
    ];
    private static readonly double[] linear = Enumerable.Range(0, 256).Select(i => i / 255.0 <= 0.04045 ? i / 255.0 / 12.92 : Math.Pow((i / 255.0 + 0.055) / 1.055, 2.4)).ToArray();
    private static byte Encode(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return (byte)Math.Round(255 * (value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055));
    }
    public static void Simulate(byte[] bgra, ColorVisionPreview mode)
    {
        if (mode == ColorVisionPreview.None) return;
        var m = matrices[(int)mode];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            double r = linear[bgra[i + 2]], g = linear[bgra[i + 1]], b = linear[bgra[i]];
            bgra[i + 2] = Encode(m[0] * r + m[1] * g + m[2] * b);
            bgra[i + 1] = Encode(m[3] * r + m[4] * g + m[5] * b);
            bgra[i] = Encode(m[6] * r + m[7] * g + m[8] * b);
        }
    }
    internal static BitmapSource Render(SceneRenderer renderer, Rect bounds, double zoom, PdfColorConverter? converter, ColorVisionPreview vision)
    {
        double scale = Math.Min(Math.Max(1, zoom), Math.Min(4096 / Math.Max(bounds.Width, bounds.Height), Math.Sqrt(4_000_000 / (bounds.Width * bounds.Height))));
        int w = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)), h = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale)); dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            if (converter is not null) dc.DrawRectangle(Brushes.White, null, renderer.OutputBounds);
            dc.DrawRectangle(SceneRenderer.Brush(renderer.Document.Canvas.Background), null, renderer.OutputBounds);
            dc.DrawDrawing(renderer.Drawing);
        }
        var rendered = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
        var original = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[w * h * 4]; original.CopyPixels(pixels, w * 4, 0);
        if (converter is not null)
        {
            var proof = new byte[pixels.Length]; converter.SoftProof(original).CopyPixels(proof, w * 4, 0);
            for (int i = 0; i < pixels.Length; i += 4) { proof[i + 3] = pixels[i + 3]; }
            pixels = proof;
        }
        Simulate(pixels, vision);
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4); bitmap.Freeze(); return bitmap;
    }
}

public sealed partial class SceneSurface
{
    internal PdfColorConverter? DisplayConverter { get; private set; }
    public ColorVisionPreview VisionPreview { get; private set; }
    public bool HasDisplayPreview => DisplayConverter is not null || VisionPreview != ColorVisionPreview.None;
    internal Action<string>? DisplayPreviewError { get; set; }
    private SceneRenderer? previewRenderer;
    private BitmapSource? displayBitmap;
    private Rect displayBounds;
    private double previewZoom;
    internal void SetDisplayPreview(PdfColorConverter? converter, ColorVisionPreview vision)
    {
        if (DisplayConverter == converter && VisionPreview == vision) return;
        DisplayConverter = converter; VisionPreview = vision; previewRenderer = null; displayBitmap = null; InvalidateVisual();
    }
    private bool DrawDisplayPreview(DrawingContext dc)
    {
        if (!HasDisplayPreview || Renderer is null) return false;
        double zoom = Math.Abs(LayoutTransform.Value.M11);
        if (previewRenderer != Renderer || zoom != previewZoom)
        {
            previewRenderer = Renderer; previewZoom = zoom; displayBitmap = null;
            displayBounds = Renderer.OutputBounds; displayBounds.Union(Renderer.Drawing.Bounds);
            try { displayBitmap = DisplayPreview.Render(Renderer, displayBounds, zoom, DisplayConverter, VisionPreview); DisplayPreviewError?.Invoke(""); }
            catch (Exception ex) { DisplayPreviewError?.Invoke("表示確認を適用できませんでした：" + ex.Message); }
        }
        if (displayBitmap is null) return false;
        dc.DrawImage(displayBitmap, displayBounds); return true;
    }
}
