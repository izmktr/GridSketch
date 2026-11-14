using System.IO;

namespace GridSketch;

public sealed class PdfSettings
{
    public bool PrintCompany { get; set; }
    public double BleedMm { get; set; }
    public bool CropMarks { get; set; }
    public bool ConvertToCmyk { get; set; }
    public bool PreserveBlack { get; set; } = true;
    public string ProfileName { get; set; } = "";
    public byte[]? ProfileData { get; set; }
    public int MaxImageDpi { get; set; } = 300;
    public bool JpegCompression { get; set; } = true;
    public int JpegQuality { get; set; } = 90;
    public string Preset => PrintCompany ? "印刷会社用" : MaxImageDpi == 300 && JpegCompression && JpegQuality == 90 ? "印刷用"
        : MaxImageDpi == 150 && JpegCompression && JpegQuality == 75 ? "軽量"
        : MaxImageDpi == 0 && !JpegCompression ? "品質優先" : "カスタム";
    public static PdfSettings ForPreset(string name) => name switch
    {
        "印刷会社用" => CompanyPreset(),
        "軽量" => new() { MaxImageDpi = 150, JpegQuality = 75 },
        "品質優先" => new() { MaxImageDpi = 0, JpegCompression = false },
        _ => new()
    };
    private static PdfSettings CompanyPreset()
    {
        var value = new PdfSettings { PrintCompany = true, ConvertToCmyk = true, BleedMm = 3, MaxImageDpi = 350, JpegCompression = false };
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "drivers", "color", "JapanColor2001Coated.icc");
        if (File.Exists(path)) { value.ProfileData = File.ReadAllBytes(path); value.ProfileName = Path.GetFileNameWithoutExtension(path); }
        return value;
    }
    public static void ValidateProfile(byte[] bytes)
    {
        if (bytes.Length < 128 || bytes.Length > 4_000_000 || System.Text.Encoding.ASCII.GetString(bytes, 36, 4) != "acsp"
            || System.Text.Encoding.ASCII.GetString(bytes, 16, 4) != "CMYK"
            || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes) != bytes.Length)
            throw new InvalidDataException("有効なCMYKのICCプロファイル（4MB以下）を選択してください。");
    }
    public void Validate()
    {
        if (!double.IsFinite(BleedMm) || BleedMm < 0 || BleedMm > 20) throw new InvalidDataException("塗り足しは0～20mmで指定してください。");
        if (ProfileName is null || ProfileName.Length > 200) throw new InvalidDataException("ICCプロファイル名が不正です。");
        if (ProfileData is not null) ValidateProfile(ProfileData);
        if (MaxImageDpi is < 0 or > 2400 || JpegQuality is < 1 or > 100) throw new InvalidDataException("PDFの画像解像度は0～2400dpi、JPEG品質は1～100で指定してください。");
    }
}
