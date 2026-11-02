using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using Microsoft.Graphics.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// One recognizer, serial requests, no image files or model ownership in the UI process.
internal static class Program
{
    private static TextRecognizer? recognizer;
    private static string stage = "request";
    private static double[] Polygon(RecognizedTextBoundingBox box) => [box.TopLeft.X, box.TopLeft.Y, box.TopRight.X, box.TopRight.Y, box.BottomRight.X, box.BottomRight.Y, box.BottomLeft.X, box.BottomLeft.Y];
    private static async Task<Response> Run(JsonElement request)
    {
        stage = "request";
        string action = request.GetProperty("action").GetString() ?? "";
        if (action == "status") return new() { Ok = true, State = TextRecognizer.GetReadyState().ToString() };
        if (action == "open")
        {
            stage = "readiness"; var state = TextRecognizer.GetReadyState();
            if (state == AIFeatureReadyState.NotReady)
            {
                stage = "prepare-model"; var prepared = await TextRecognizer.EnsureReadyAsync();
                if (prepared.Status != AIFeatureReadyResultState.Success) return new() { Error = "model-not-ready", Detail = prepared.ErrorDisplayText, Stage = stage };
            }
            else if (state != AIFeatureReadyState.Ready) return new() { Error = state.ToString(), Stage = stage };
            stage = "create-model"; recognizer ??= await TextRecognizer.CreateAsync();
            return new() { Ok = true, State = "Ready" };
        }
        if (action != "recognize" || recognizer is null) return new() { Error = "not-open" };
        stage = "decode-image";
        var bytes = Convert.FromBase64String(request.GetProperty("png").GetString() ?? "");
        if (bytes.Length is < 24 or > 24 * 1024 * 1024) return new() { Error = "image-limit" };
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if ((long)decoder.PixelWidth * decoder.PixelHeight > 4_000_000) return new() { Error = "image-limit" };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var buffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);
        stage = "recognize"; var result = recognizer.RecognizeTextFromImage(buffer);
        stage = "read-result";
        var lines = result?.Lines?.Take(10000).Select(line => new OcrLine {
            Text = line.Text ?? "", Polygon = Polygon(line.BoundingBox),
            Words = line.Words?.Take(2000).Select(word => new OcrWord { Text = word.Text ?? "", Polygon = Polygon(word.BoundingBox) }).ToArray() ?? []
        }).ToArray() ?? [];
        return new() { Ok = true, Data = new() { Width = decoder.PixelWidth, Height = decoder.PixelHeight, Lines = lines } };
    }
    private static async Task Main()
    {
        Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            while (await Console.In.ReadLineAsync() is string line)
            {
                Response response;
                try
                {
                    if (line.Length > 34 * 1024 * 1024) response = new() { Error = "request-limit" };
                    else { using var request = JsonDocument.Parse(line); response = await Run(request.RootElement); }
                }
                catch (Exception ex) { response = new() { Error = ex.HResult == unchecked((int)0x80070005) ? "windows-ai-identity" : "windows-ai-unavailable", Hresult = $"0x{ex.HResult:X8}", Stage = stage, Detail = ex.Message }; }
                Console.WriteLine(JsonSerializer.Serialize(response, OcrJson.Default.Response));
            }
        }
        finally { recognizer?.Dispose(); }
    }
}
internal sealed class Response
{
    public bool Ok { get; set; }
    public string? State { get; set; }
    public string? Error { get; set; }
    public string? Detail { get; set; }
    public string? Stage { get; set; }
    public string? Hresult { get; set; }
    public OcrData? Data { get; set; }
}
internal class OcrWord { public string Text { get; set; } = ""; public double[] Polygon { get; set; } = []; }
internal sealed class OcrLine : OcrWord { public OcrWord[] Words { get; set; } = []; }
internal sealed class OcrData { public uint Width { get; set; } public uint Height { get; set; } public OcrLine[] Lines { get; set; } = []; }
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Response))]
internal partial class OcrJson : JsonSerializerContext { }
