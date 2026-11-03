// 합성 HTTP·GPS로 지도 선택/캐시/취소를 검사하고 실제 기본 지도는 한 화면만 확인합니다.
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal static class MapSmokeTest
{
    private sealed class Tiles(byte[] png) : HttpMessageHandler
    {
        internal int Requests;
        internal bool Conditional;
        internal string Mode = "ok";
        internal int Delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Requests++;
            Check(request.RequestUri is { Scheme: "https", Host: "tile.openstreetmap.org", Query: "" }
                && request.Headers.UserAgent.ToString().StartsWith("MyPhoto/") && request.Method == HttpMethod.Get
                && request.Content is null && request.Headers.CacheControl is null, "identified tile-only HTTPS request");
            Conditional |= request.Headers.IfNoneMatch.Any(tag => tag.Tag == "\"fixture\"");
            if (Delay > 0) await Task.Delay(Delay, cancellation);
            var response = new HttpResponseMessage(Mode == "304" ? HttpStatusCode.NotModified : Mode == "503" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new ByteArrayContent(Mode == "html" ? "not a PNG"u8.ToArray() : png) };
            if (Mode != "304")
            {
                response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromHours(1), NoStore = Mode == "no-store" };
                response.Headers.ETag = new EntityTagHeaderValue("\"fixture\"");
                response.Content.Headers.LastModified = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
            }
            return response;
        }
    }

    internal static async Task RunAsync(MainWindow window, string output, bool live)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("test-artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output)) throw new IOException("Use a new test-artifacts output.");
        for (var parent = Directory.GetParent(output); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("No linked test paths.");
        Directory.CreateDirectory(output);
        var panel = window.MapPanel;
        if (live)
        {
            panel.Configure(true, Path.Combine(output, "live-cache"));
            await panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await panel.RefreshNowAsync();
            Check(panel.VisibleTileCount > 0 && !panel.HasMarker, "live public default map renders without user GPS");
            Capture(window, Path.Combine(output, "map-live.png"));
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: single public default viewport; no user photos or GPS.");
            return;
        }
        byte[] png;
        using (var image = new MagickImage(MagickColors.PaleGreen, 256, 256)) png = image.ToByteArray(MagickFormat.Png);
        using var handler = new Tiles(png);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var cacheRoot = Path.Combine(output, "cache-check");
        var cache = new MapTileCache(cacheRoot, client);
        await cache.GetAsync(5, 27, 12, true, default);
        await cache.GetAsync(5, 27, 12, true, default);
        Check(handler.Requests == 1, "fresh cache prevents repeat downloads");
        var metadataFile = Path.Combine(cacheRoot, "5", "27", "12.png.json");
        var metadata = JsonNode.Parse(File.ReadAllText(metadataFile))!;
        metadata["Expires"] = DateTimeOffset.UtcNow.AddDays(-1);
        File.WriteAllText(metadataFile, metadata.ToJsonString());
        handler.Mode = "304";
        await cache.GetAsync(5, 27, 12, true, default);
        Check(handler.Requests == 2 && handler.Conditional && JsonNode.Parse(File.ReadAllText(metadataFile))!["Freshness"]!.GetValue<string>() == "01:00:00",
            "expired tile uses ETag and preserves caching policy on 304");
        await cache.GetAsync(5, 27, 12, false, default);
        Check(handler.Requests == 2, "offline revisit uses cache without requests");
        handler.Mode = "no-store";
        await cache.GetAsync(5, 28, 12, true, default);
        Check(!File.Exists(Path.Combine(cacheRoot, "5", "28", "12.png")), "no-store response not persisted");
        handler.Mode = "html";
        try { await cache.GetAsync(5, 29, 12, true, default); throw new Exception("invalid PNG accepted"); }
        catch (IOException) { Check(true, "HTML response rejected as tile"); }
        foreach (var point in new[] { new GpsCoordinates(0, 0), new GpsCoordinates(-35, -120), new GpsCoordinates(37.5, 126.25) })
        {
            var roundtrip = MapMath.Location(MapMath.World(point, 13), 13);
            Check(Math.Abs(point.Latitude - roundtrip.Latitude) < 1e-8 && Math.Abs(point.Longitude - roundtrip.Longitude) < 1e-8, "map projection roundtrip");
        }
        Check(MapMath.Visible(new(0, 180), 3, 600, 120).All(tile => tile.X is >= 0 and < 8 && tile.Y is >= 0 and < 8), "dateline tiles wrap safely");
        Check(double.IsFinite(MapMath.World(new(90, 0), 4).Y), "polar projection bounded");
        handler.Mode = "ok";
        panel.Configure(true, Path.Combine(output, "ui-cache"), client);
        var root = Path.Combine(output, "합성 사진"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "gps.jpg");
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 300, 200))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.GPSLatitudeRef, "N"); exif.SetValue(ExifTag.GPSLongitudeRef, "E");
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(37u), new Rational(30u), new Rational(0u) });
            exif.SetValue(ExifTag.GPSLongitude, new[] { new Rational(126u), new Rational(15u), new Rational(0u) });
            exif.SetValue(ExifTag.GPSMapDatum, "WGS-84"); image.SetProfile(exif); image.Write(path);
        }
        using (var image = new MagickImage(MagickColors.Beige, 300, 200)) image.Write(Path.Combine(root, "plain.png"));
        var hashes = Directory.GetFiles(root).ToDictionary(file => file, file => SHA256.HashData(File.ReadAllBytes(file)));
        await window.ScanFolderAsync(root);
        var gps = window.PhotoList.Items.Cast<PhotoItem>().Single(item => item.Record.RelativePath == "gps.jpg");
        var plain = window.PhotoList.Items.Cast<PhotoItem>().Single(item => item.Record.RelativePath == "plain.png");
        window.PhotoList.SelectedItem = gps; await window.PreviewTask; await panel.RefreshNowAsync();
        Check(panel.HasMarker && panel.VisibleTileCount > 0 && panel.LocationLabel.Text.Contains("37.50000"), "selected photo GPS displays pin and tiles");
        var zoom = panel.ZoomLevel; panel.ZoomIn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await panel.RefreshNowAsync(); Check(panel.ZoomLevel == zoom + 1 && panel.HasMarker, "map zoom preserves photo marker");
        window.PhotoList.SelectedItem = plain; await window.PreviewTask; await panel.RefreshNowAsync();
        Check(!panel.HasMarker && panel.LocationLabel.Text.Contains("정보 없음"), "no-GPS photo clears previous marker");
        panel.SetPhoto(gps.Record with { RelativePath = "video.MOV" });
        Check(!panel.HasMarker && panel.LocationLabel.Text.Contains("미추출"), "video does not reuse photo GPS");
        panel.SetPhoto(gps.Record with { Error = "INVALID_IMAGE" }); Check(!panel.HasMarker, "error row clears marker");
        var json = JsonNode.Parse(gps.Record.ExifJson)!; json["GPSMapDatum"] = "Tokyo";
        panel.SetPhoto(gps.Record with { ExifJson = json.ToJsonString() }); Check(!panel.HasMarker, "unsupported datum not silently mapped");
        json.AsObject().Remove("GPSMapDatum"); panel.SetPhoto(gps.Record with { ExifJson = json.ToJsonString() });
        Check(panel.HasMarker && panel.LocationLabel.Text.Contains("미확인"), "missing datum explicitly provisional");
        handler.Delay = 150;
        panel.Configure(true, Path.Combine(output, "race-cache"), client);
        panel.SetPhoto(gps.Record); var old = panel.RefreshNowAsync();
        panel.SetPhoto(null); await panel.RefreshNowAsync(); await old;
        Check(!panel.HasMarker && !panel.Layer.Children.OfType<System.Windows.Shapes.Path>().Any(), "late tile response cannot restore old selection");
        handler.Delay = 0; handler.Mode = "503";
        panel.Configure(true, Path.Combine(output, "failure-cache"), client); await panel.RefreshNowAsync();
        Check(panel.VisibleTileCount == 0 && panel.TileStatus.Text.Contains("실패"), "network failure stays visible without crashing photo UI");
        handler.Mode = "ok"; panel.Configure(true, Path.Combine(output, "ui-cache"), client);
        window.PhotoList.SelectedItem = gps; await window.PreviewTask; await panel.RefreshNowAsync();
        panel.TileStatus.Text = "합성 지도 타일 · 실제 지도 아님";
        Capture(window, Path.Combine(output, "map-synthetic.png"));
        Check(hashes.All(pair => pair.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pair.Key)))), "synthetic source hashes unchanged");
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: map GPS, HTTP caching, projection and UI; fake HTTP only.");
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
