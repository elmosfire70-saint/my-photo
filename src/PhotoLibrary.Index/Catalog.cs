// 원본 폴더를 읽기 전용으로 수집하고 SQLite에서 날짜별 사진 목록을 조회합니다.
using System.Globalization;
using System.Text.Json;
using ImageMagick;
using Microsoft.Data.Sqlite;

namespace PhotoLibrary.Index;

public sealed record ScanResult(int Written, int Skipped, int Errors);
public sealed record MediaRow(string Root, string RelativePath, string DateText, string DateSource,
    string? Offset, long? Width, long? Height, string ExifJson, string ProfilesJson, string? Error);

public static class Catalog
{
    private const int ApplicationId = 0x50484F54;
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".avif", ".heic", ".heif" };
    private static readonly HashSet<string> Unverified = new(StringComparer.OrdinalIgnoreCase)
        { ".cr2", ".cr3", ".nef", ".arw", ".dng" };

    public static bool IsVideo(string path) => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase);

    public static string ResolveMediaPath(string root, string relativePath)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relativePath) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("선택한 사진 폴더 밖의 파일은 열지 않습니다.");
        RejectLinks(path);
        if (!File.Exists(path)) throw new FileNotFoundException("미디어 파일을 찾을 수 없습니다.");
        return path;
    }

    public static ScanResult Scan(string root, string database, CancellationToken cancellation = default,
        Action<ScanResult>? progress = null)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        database = Path.GetFullPath(database);
        RejectLinks(root);
        RejectLinks(database);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Source root is unavailable; records are retained.");
        if (database.Equals(root, StringComparison.OrdinalIgnoreCase)
            || database.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Database must be outside the source root.");
        cancellation.ThrowIfCancellationRequested();
        using var connection = Open(database, create: true);
        var result = new ScanResult(0, 0, 0);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            cancellation.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(path);
            if (!Supported.Contains(extension) && !Unverified.Contains(extension) && !IsVideo(path)) continue;
            var relative = Path.GetRelativePath(root, path);
            var file = new FileInfo(path);
            var length = file.Length;
            var modified = file.LastWriteTimeUtc;
            using var existing = Command(connection,
                "SELECT COUNT(*) FROM media WHERE root=$root AND relative_path=$path AND length=$length AND modified_ticks=$ticks AND error IS NULL",
                ("$root", root), ("$path", relative), ("$length", length), ("$ticks", modified.Ticks));
            // ponytail: 크기·수정시각이 같은 내용 변경은 놓칩니다. 엄밀한 변경 감지는 후속 해시 검사로 보완합니다.
            if (Convert.ToInt64(existing.ExecuteScalar()) == 1)
            {
                result = result with { Skipped = result.Skipped + 1 };
                progress?.Invoke(result);
                continue;
            }
            var date = modified.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            var dateSource = "file-modified-utc-estimated";
            string? offset = null;
            long? width = null, height = null;
            var exifJson = "{}";
            var profilesJson = "{}";
            string? error = null;
            if (Unverified.Contains(extension)) error = "UNVERIFIED_FORMAT";
            else if (IsVideo(path))
                exifJson = """{"MediaKind":"video","MetadataStatus":"capture-date-and-gps-not-extracted"}""";
            else
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var image = new MagickImage();
                    image.Ping(stream);
                    width = image.Width;
                    height = image.Height;
                    var exif = image.GetExifProfile();
                    exifJson = JsonSerializer.Serialize(exif?.Values.ToDictionary(v => v.Tag.ToString(), v => v.GetValue())
                        ?? new Dictionary<string, object>());
                    profilesJson = JsonSerializer.Serialize(image.ProfileNames.ToDictionary(name => name,
                        name => Convert.ToBase64String(image.GetProfile(name)!.ToByteArray())));
                    var captured = exif?.GetValue(ExifTag.DateTimeOriginal)?.Value;
                    offset = exif?.GetValue(ExifTag.OffsetTimeOriginal)?.Value;
                    if (DateTime.TryParseExact(captured, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                    {
                        date = parsed.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                        dateSource = "exif-original-wall-clock";
                    }
                    file.Refresh();
                    if (!file.Exists || file.Length != length || file.LastWriteTimeUtc != modified)
                        throw new IOException("Source changed during metadata read.");
                }
                catch (Exception exception) when (exception is MagickException or IOException or UnauthorizedAccessException)
                {
                    error = exception is MagickException and not MagickBlobErrorException ? "INVALID_IMAGE" : "READ_FAILED_OR_CHANGED";
                    width = height = null;
                    exifJson = profilesJson = "{}";
                    date = modified.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                    dateSource = "file-modified-utc-estimated";
                    offset = null;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            using var save = Command(connection, """
                INSERT INTO media(root, relative_path, length, modified_ticks, width, height,
                    date_text, date_source, offset_text, exif_json, profiles_json, error)
                VALUES ($root, $path, $length, $ticks, $width, $height, $date, $source, $offset, $exif, $profiles, $error)
                ON CONFLICT(root, relative_path) DO UPDATE SET length=excluded.length,
                    modified_ticks=excluded.modified_ticks, width=excluded.width, height=excluded.height,
                    date_text=excluded.date_text, date_source=excluded.date_source, offset_text=excluded.offset_text,
                    exif_json=excluded.exif_json, profiles_json=excluded.profiles_json, error=excluded.error
                """, ("$root", root), ("$path", relative), ("$length", length), ("$ticks", modified.Ticks),
                ("$width", width), ("$height", height), ("$date", date), ("$source", dateSource),
                ("$offset", offset), ("$exif", exifJson), ("$profiles", profilesJson), ("$error", error));
            // 한 문장 자동 커밋으로 취소·프로세스 종료 전 완료된 항목을 재사용합니다.
            save.ExecuteNonQuery();
            result = result with { Written = result.Written + 1, Errors = result.Errors + (error is null ? 0 : 1) };
            progress?.Invoke(result);
        }
        return result;
    }

    public static IReadOnlyList<MediaRow> List(string database, DateOnly? from = null, DateOnly? to = null, int limit = 200,
        string? root = null, int offset = 0)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (from > to) throw new ArgumentException("From date must not exceed to date.");
        using var connection = Open(Path.GetFullPath(database), create: false);
        // ponytail: OFFSET은 뒤쪽 페이지에서 느려집니다. 대규모 검증 단계에서 키셋 페이지로 교체합니다.
        using var command = Command(connection, """
            SELECT root,relative_path,date_text,date_source,offset_text,width,height,exif_json,profiles_json,error
            FROM media WHERE ($from IS NULL OR date_text >= $from) AND ($to IS NULL OR date_text <= $to)
                AND ($root IS NULL OR root=$root)
            ORDER BY date_text DESC,root,relative_path LIMIT $limit OFFSET $offset
            """, ("$from", from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$to", to is { } lastDate ? lastDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59.9999999" : null),
            ("$limit", limit), ("$root", root is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))),
            ("$offset", offset));
        using var reader = command.ExecuteReader();
        var rows = new List<MediaRow>();
        while (reader.Read()) rows.Add(new MediaRow(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return rows;
    }

    private static SqliteConnection Open(string path, bool create)
    {
        RejectLinks(path);
        var existed = File.Exists(path);
        if (create) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            connection.CreateCollation("WINPATH", (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left, right));
            using var identity = Command(connection, "PRAGMA application_id");
            using var version = Command(connection, "PRAGMA user_version");
            if (existed || !create)
            {
                if (Convert.ToInt32(identity.ExecuteScalar()) != ApplicationId || Convert.ToInt32(version.ExecuteScalar()) != 1)
                    throw new IOException("Not a supported photo catalog; existing database was not modified.");
            }
            else
            {
                using var initialize = Command(connection, $"""
                    BEGIN;
                    PRAGMA application_id={ApplicationId};
                    PRAGMA user_version=1;
                    CREATE TABLE media(root TEXT COLLATE WINPATH NOT NULL, relative_path TEXT COLLATE WINPATH NOT NULL,
                        length INTEGER NOT NULL, modified_ticks INTEGER NOT NULL, width INTEGER, height INTEGER,
                        date_text TEXT NOT NULL, date_source TEXT NOT NULL, offset_text TEXT,
                        exif_json TEXT NOT NULL, profiles_json TEXT NOT NULL, error TEXT,
                        PRIMARY KEY(root,relative_path));
                    CREATE INDEX media_date ON media(date_text);
                    COMMIT;
                    """);
                initialize.ExecuteNonQuery();
            }
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }

    private static void RejectLinks(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName))
                && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and junctions are not supported in catalog paths.");
    }
}
