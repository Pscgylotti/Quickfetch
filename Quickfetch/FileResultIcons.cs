using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Quickfetch.Core;

namespace Quickfetch;

// A finite set of frozen vectors: no Shell requests, file reads or per-result bitmaps.
internal static class FileResultIcons
{
    private static readonly IReadOnlyDictionary<string, ImageSource> Cache = Create();
    internal static ImageSource Get(FileEntry file) => Cache[file.IsFolder ? "folder" : Path.GetExtension(file.Name).ToLowerInvariant() switch
    {
        ".txt" or ".md" or ".log" or ".rtf" or ".doc" or ".docx" or ".odt" => "document",
        ".pdf" => "pdf",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".heic" or ".tif" or ".tiff" => "image",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" => "video",
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" => "audio",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz" => "archive",
        ".cs" or ".cpp" or ".h" or ".py" or ".js" or ".ts" or ".tsx" or ".jsx" or ".json" or ".xml" or ".html" or ".css" or ".yaml" or ".yml" or ".toml" or ".ps1" => "code",
        ".exe" or ".msi" or ".lnk" or ".appx" or ".msix" => "app",
        ".xls" or ".xlsx" or ".csv" or ".ods" => "sheet",
        ".ppt" or ".pptx" or ".odp" => "slides",
        _ => "file"
    }];
    private static IReadOnlyDictionary<string, ImageSource> Create()
    {
        var result = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        foreach (var (kind, color, badge) in new[] {
            ("folder", "#F4C75A", ""), ("file", "#8E9EB4", ""),
            ("document", "#4187D8", "TXT"), ("pdf", "#D85862", "PDF"),
            ("image", "#A271D6", "IMG"), ("video", "#7C79DA", "VID"),
            ("audio", "#319CA9", "SND"), ("archive", "#C78B39", "ZIP"),
            ("code", "#469B83", "{ }"), ("app", "#4A8DAE", "APP"),
            ("sheet", "#45935F", "XLS"), ("slides", "#CE7B48", "PPT") })
        {
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 32, 32));
                var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                if (kind == "folder")
                {
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(187, 137, 43)), null, Geometry.Parse("M2,7 L13,7 L16,11 L29,11 L29,28 L3,28 Z"));
                    dc.DrawGeometry(accent, null, Geometry.Parse("M2,13 L30,13 L28,29 L4,29 Z"));
                }
                else
                {
                    var outline = new Pen(new SolidColorBrush(Color.FromRgb(155, 172, 194)), 1);
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(233, 240, 249)), outline, Geometry.Parse("M7,2 L21,2 L28,9 L28,29 L7,29 Z"));
                    dc.DrawGeometry(null, outline, Geometry.Parse("M21,2 L21,9 L28,9"));
                    if (badge.Length > 0)
                    {
                        dc.DrawRoundedRectangle(accent, null, new Rect(2, 15, 28, 13), 3, 3);
                        var text = new FormattedText(badge, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 9, Brushes.White, 1);
                        dc.DrawGeometry(Brushes.White, null, text.BuildGeometry(new Point(16 - text.Width / 2, 21.5 - text.Height / 2)));
                    }
                    else
                        foreach (var y in new[] { 15, 19, 23 }) dc.DrawLine(new Pen(accent, 1.4), new Point(12, y), new Point(23, y));
                }
            }
            drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); result.Add(kind, image);
        }
        return result;
    }
}
