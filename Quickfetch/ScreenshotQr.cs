using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;

namespace Quickfetch;

internal static class ScreenshotQr
{
    internal static string? Decode(BitmapSource image)
    {
        var gray = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        var pixels = new byte[checked(gray.PixelWidth * gray.PixelHeight)];
        gray.CopyPixels(pixels, gray.PixelWidth, 0);
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        reader.Options.TryHarder = true;
        reader.Options.TryInverted = true;
        return reader.Decode(pixels, gray.PixelWidth, gray.PixelHeight, RGBLuminanceSource.BitmapFormat.Gray8)?.Text;
    }

    internal static Uri? WebLink(string content) =>
        Uri.TryCreate(content, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.Host.Length > 0 ? uri : null;
}
