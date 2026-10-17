using System.IO;

namespace GridSketch;

public sealed class PdfSettings
{
    public int MaxImageDpi { get; set; } = 300;
    public bool JpegCompression { get; set; } = true;
    public int JpegQuality { get; set; } = 90;
    public string Preset => MaxImageDpi == 300 && JpegCompression && JpegQuality == 90 ? "印刷用"
        : MaxImageDpi == 150 && JpegCompression && JpegQuality == 75 ? "軽量"
        : MaxImageDpi == 0 && !JpegCompression ? "品質優先" : "カスタム";
    public static PdfSettings ForPreset(string name) => name switch
    {
        "軽量" => new() { MaxImageDpi = 150, JpegQuality = 75 },
        "品質優先" => new() { MaxImageDpi = 0, JpegCompression = false },
        _ => new()
    };
    public void Validate()
    {
        if (MaxImageDpi is < 0 or > 2400 || JpegQuality is < 1 or > 100) throw new InvalidDataException("PDFの画像解像度は0～2400dpi、JPEG品質は1～100で指定してください。");
    }
}
