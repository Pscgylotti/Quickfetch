using System;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Quickfetch;

internal sealed class CaptureStorage(Settings settings, Action<Settings> saveSettings)
{
    internal string? Save(BitmapSource image, bool saveAs)
    {
        var folder = settings.CaptureFolder;
        if (!Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string path;
        if (saveAs || string.IsNullOrWhiteSpace(settings.CaptureFolder) || !Directory.Exists(settings.CaptureFolder))
        {
            var dialog = new SaveFileDialog { Title = Loc.T("保存截图"), Filter = "PNG (*.png)|*.png", DefaultExt = ".png", AddExtension = true, InitialDirectory = folder, FileName = Name() };
            if (dialog.ShowDialog() != true) return null;
            path = dialog.FileName;
            Write(image, path, overwrite: true);
        }
        else
        {
            path = Path.Combine(folder, Name());
            Write(image, path, overwrite: false);
        }
        settings.CaptureFolder = Path.GetDirectoryName(path)!;
        try { saveSettings(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppDiagnostics.Current.Fault("capture-folder-save", ex); }
        return path;
    }
    private static string Name() => "Quickfetch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..5] + ".png";
    internal static void Write(BitmapSource image, string path, bool overwrite)
    {
        // Encode before opening the destination: encoding failure cannot truncate an existing file.
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var data = new MemoryStream(); encoder.Save(data); data.Position = 0;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) data.CopyTo(file);
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
