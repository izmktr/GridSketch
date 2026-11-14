using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GridSketch;

// Windows color management performs the ICC conversion; never approximate CMYK with a formula.
internal sealed class PdfColorConverter
{
    private readonly ColorContext destination;
    private readonly ColorContext source = new(PixelFormats.Bgra32);
    private readonly Dictionary<uint, byte[]> colors = [];
    public PdfColorConverter(byte[] profile)
    {
        PdfSettings.ValidateProfile(profile);
        // ColorContext keeps its profile file open until native handles are collected.
        // Reuse a content-addressed temporary cache instead of deleting a live file.
        var folder = Path.Combine(Path.GetTempPath(), "GridSketch-ICC");
        Directory.CreateDirectory(folder);
        var cached = Path.Combine(folder, System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(profile)) + ".icc");
        try
        {
            using var file = new FileStream(cached, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(profile);
        }
        catch (IOException) when (File.Exists(cached))
        {
            if (!File.ReadAllBytes(cached).SequenceEqual(profile)) throw new InvalidDataException("ICCキャッシュが破損しています。");
        }
        destination = new ColorContext(new Uri(cached));
    }
    public byte[] Convert(BitmapSource bitmap)
    {
        var converted = new ColorConvertedBitmap(bitmap, source, destination, PixelFormats.Cmyk32);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }
    public byte[] Convert(Color color)
    {
        uint key = (uint)(color.R << 16 | color.G << 8 | color.B);
        if (!colors.TryGetValue(key, out var result))
        {
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { color.B, color.G, color.R, 255 }, 4);
            colors[key] = result = Convert(bitmap);
        }
        return result;
    }
}
