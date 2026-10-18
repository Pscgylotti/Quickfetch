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
            ("folder", "#F5BD45", ""), ("file", "#8E9EB4", ""),
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
                var tone = (Color)ColorConverter.ConvertFromString(color);
                var accent = new SolidColorBrush(tone);
                if (kind == "folder")
                {
                    // Back sheet with tab, then a lighter front pocket lit from the top.
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(214, 158, 46)), null, Geometry.Parse("M3,8 A2,2 0 0 1 5,6 L11.6,6 A2,2 0 0 1 13,6.6 L15,8.6 L27,8.6 A2,2 0 0 1 29,10.6 L29,25 A2,2 0 0 1 27,27 L5,27 A2,2 0 0 1 3,25 Z"));
                    var front = new LinearGradientBrush(Color.FromRgb(255, 214, 112), tone, 90);
                    dc.DrawRoundedRectangle(front, null, new Rect(3, 11.5, 26, 15.5), 2, 2);
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 240, 196)), 1), new Point(4.5, 12.5), new Point(27.5, 12.5));
                }
                else
                {
                    // Rounded page with a soft folded corner; the type reads from a colored pill.
                    var outline = new Pen(new SolidColorBrush(Color.FromRgb(170, 184, 204)), 1);
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(247, 249, 252)), outline, Geometry.Parse("M9,2.5 L19.5,2.5 L26.5,9.5 L26.5,27 A2.5,2.5 0 0 1 24,29.5 L9,29.5 A2.5,2.5 0 0 1 6.5,27 L6.5,5 A2.5,2.5 0 0 1 9,2.5 Z"));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(222, 229, 239)), outline, Geometry.Parse("M19.5,2.5 L19.5,7.5 A2,2 0 0 0 21.5,9.5 L26.5,9.5 Z"));
                    if (badge.Length > 0)
                    {
                        dc.DrawRoundedRectangle(accent, null, new Rect(3, 16, 22, 10.5), 2.5, 2.5);
                        var text = new FormattedText(badge, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 7.5, Brushes.White, 1);
                        dc.DrawGeometry(Brushes.White, null, text.BuildGeometry(new Point(14 - text.Width / 2, 21.25 - text.Height / 2)));
                    }
                    else
                    {
                        var line = new Pen(accent, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        foreach (var (y, end) in new[] { (15.5, 22.5), (19.5, 22.5), (23.5, 18.5) }) dc.DrawLine(line, new Point(10.5, y), new Point(end, y));
                    }
                }
            }
            drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); result.Add(kind, image);
        }
        return result;
    }
}
