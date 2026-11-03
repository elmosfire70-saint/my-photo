// 지도 타일을 제한된 HTTPS 요청과 서버 캐시·조건부 검증으로 읽고 앱 전용 캐시에 보관합니다.
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace PhotoLibrary.Desktop;

internal sealed class MapTileCache(string root, HttpClient client)
{
    private readonly string _root = Path.GetFullPath(root);
    private readonly SemaphoreSlim _requests = new(4);
    private sealed record Entry(DateTimeOffset Expires, string? ETag, DateTimeOffset? Modified, bool Revalidate,
        TimeSpan Freshness, bool AlwaysValidate);

    internal static BitmapSource Decode(byte[] bytes)
    {
        if (bytes.Length is < 24 or > 1_048_576 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) != 256
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) != 256) throw new IOException("유효한 256px PNG 지도 타일이 아닙니다.");
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
        image.Freeze();
        return image;
    }

    internal async Task<BitmapSource> GetAsync(int zoom, int x, int y, bool online, CancellationToken cancellation)
    {
        if (zoom is < 2 or > 18 || x < 0 || y < 0 || x >= 1 << zoom || y >= 1 << zoom) throw new ArgumentOutOfRangeException(nameof(zoom));
        var file = Path.Combine(_root, zoom.ToString(), x.ToString(), y + ".png");
        Entry? entry = null;
        BitmapSource? cached = null;
        try
        {
            entry = JsonSerializer.Deserialize<Entry>(await File.ReadAllTextAsync(file + ".json", cancellation));
            var info = new FileInfo(file);
            if (info.Length > 1_048_576) throw new IOException("지도 캐시 크기 오류");
            cached = Decode(await File.ReadAllBytesAsync(file, cancellation));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or FormatException)
        { entry = null; cached = null; }
        if (cached is not null && entry is not null && (entry.Expires > DateTimeOffset.UtcNow || !online && !entry.Revalidate)) return cached;
        if (!online) throw new IOException("사용할 수 있는 지도 캐시가 없습니다.");
        await _requests.WaitAsync(cancellation);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            cancellation = deadline.Token;
            using var request = new HttpRequestMessage(HttpMethod.Get, FormattableString.Invariant($"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png"));
            request.Headers.UserAgent.ParseAdd("MyPhoto/0.1 (+https://github.com/elmosfire70-saint/my-photo)");
            request.Headers.Accept.ParseAdd("image/png");
            if (cached is not null && entry is not null)
            {
                if (EntityTagHeaderValue.TryParse(entry.ETag, out var etag)) request.Headers.IfNoneMatch.Add(etag);
                request.Headers.IfModifiedSince = entry.Modified;
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            byte[]? bytes = null;
            BitmapSource image;
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) image = cached;
            else
            {
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(cancellation);
                using var buffer = new MemoryStream();
                var block = new byte[8192];
                int length;
                while ((length = await body.ReadAsync(block, cancellation)) > 0)
                {
                    if (buffer.Length + length > 1_048_576) throw new IOException("지도 응답 크기 제한 초과");
                    buffer.Write(block, 0, length);
                }
                bytes = buffer.ToArray(); image = Decode(bytes);
            }
            var now = DateTimeOffset.UtcNow;
            var control = response.Headers.CacheControl;
            var validated = response.StatusCode == HttpStatusCode.NotModified;
            var age = TimeSpan.FromTicks(Math.Max(0, Math.Max((response.Headers.Age ?? TimeSpan.Zero).Ticks,
                (now - (response.Headers.Date ?? now)).Ticks)));
            var freshness = control?.MaxAge ?? (response.Content.Headers.Expires is { } expires
                ? expires - (response.Headers.Date ?? now) : validated && entry is not null ? entry.Freshness : TimeSpan.FromDays(7));
            var alwaysValidate = control is null && validated ? entry?.AlwaysValidate == true : control?.NoCache == true;
            var next = new Entry(alwaysValidate ? now : now + freshness - age,
                response.Headers.ETag?.ToString() ?? (validated ? entry?.ETag : null),
                response.Content.Headers.LastModified ?? (validated ? entry?.Modified : null),
                control is null && validated ? entry?.Revalidate == true : control?.MustRevalidate == true || alwaysValidate,
                freshness, alwaysValidate);
            if (control?.NoStore != true)
            {
                if (bytes is not null) await SaveAsync(file, bytes, cancellation);
                await SaveAsync(file + ".json", JsonSerializer.SerializeToUtf8Bytes(next), cancellation);
            }
            else { if (File.Exists(file)) File.Delete(file); if (File.Exists(file + ".json")) File.Delete(file + ".json"); }
            return image;
        }
        finally { _requests.Release(); }
    }

    private static async Task SaveAsync(string path, byte[] bytes, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes, cancellation); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
