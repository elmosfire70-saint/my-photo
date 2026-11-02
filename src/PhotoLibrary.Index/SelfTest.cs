// 합성 이미지로 DB 수집·중단/재개·날짜 검색과 원본 불변성을 검증합니다.
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Microsoft.Data.Sqlite;

namespace PhotoLibrary.Index;

internal static class SelfTest
{
    internal static void Run(string output)
    {
        output = Path.GetFullPath(output);
        var artifacts = Path.GetFullPath("test-artifacts") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
            throw new ArgumentException("Self-test needs a new directory under test-artifacts.");
        for (var parent = Directory.GetParent(output); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Self-test does not follow links or junctions.");
        var root = Path.Combine(output, "한글 사진 ä");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        var jpeg = Path.Combine(root, "촬영 A.JPG");
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 64, 48))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2020:01:02 03:04:05");
            exif.SetValue(ExifTag.OffsetTimeOriginal, "+09:00");
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(32u), new Rational(30u), new Rational(0u) });
            image.SetProfile(exif);
            image.Write(jpeg);
        }
        File.SetAttributes(jpeg, File.GetAttributes(jpeg) | FileAttributes.ReadOnly);
        var png = Path.Combine(root, "nested", "fallback.png");
        using (var image = new MagickImage(MagickColors.Red, 32, 24)) image.Write(png);
        var invalidDate = Path.Combine(root, "invalid-date.jpg");
        using (var image = new MagickImage(MagickColors.Blue, 16, 12))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "not-a-date");
            image.SetProfile(exif);
            image.Write(invalidDate);
        }
        var modified = new DateTime(2021, 7, 2, 12, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(png, modified);
        File.SetLastWriteTimeUtc(invalidDate, modified);
        File.WriteAllText(Path.Combine(root, "broken.jpg"), "not an image");
        File.WriteAllText(Path.Combine(root, "unverified.cr2"), "synthetic placeholder, not a RAW fixture");
        File.WriteAllText(Path.Combine(root, "ignored.txt"), "not media");
        var hashes = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var database = Path.Combine(output, "catalog.sqlite");
        using (var cancellation = new CancellationTokenSource())
        {
            try
            {
                Catalog.Scan(root, database, cancellation.Token, state => { if (state.Written > state.Errors) cancellation.Cancel(); });
                throw new Exception("FAIL: expected cancellation");
            }
            catch (OperationCanceledException) { }
        }
        var checkpoints = Catalog.List(database);
        var completed = checkpoints.Count(row => row.Error is null);
        Check(checkpoints.Count is > 0 and < 5 && completed == 1, "committed checkpoints after cancellation");
        var resumed = Catalog.Scan(root, database);
        Check(resumed.Written == 5 - completed && resumed.Skipped == completed, "resume skips committed healthy item and retries errors");
        var rows = Catalog.List(database);
        Check(rows.Count == 5 && rows.Count(row => row.Error is not null) == 2, "corrupt and unverified files isolated");
        Check(rows.Any(row => row.Error == "UNVERIFIED_FORMAT") && rows.Any(row => row.Error == "INVALID_IMAGE"), "distinct error reasons");
        var captured = rows.Single(row => row.RelativePath == "촬영 A.JPG");
        Check(captured.DateText == "2020-01-02T03:04:05.0000000" && captured.DateSource == "exif-original-wall-clock"
            && captured.Offset == "+09:00" && captured.Width == 64 && captured.Height == 48, "capture date provenance and dimensions");
        using (var tags = JsonDocument.Parse(captured.ExifJson))
            Check(tags.RootElement.GetProperty("GPSLatitude")[1].GetProperty("Numerator").GetUInt32() == 30,
                "EXIF arrays stored as values, not type names");
        using (var profiles = JsonDocument.Parse(captured.ProfilesJson))
        {
            var exif = new ExifProfile(Convert.FromBase64String(profiles.RootElement.GetProperty("exif").GetString()!));
            Check(exif.GetValue(ExifTag.DateTimeOriginal)?.Value == "2020:01:02 03:04:05"
                && exif.GetValue(ExifTag.GPSLatitude)?.Value[1] == new Rational(30u), "EXIF profile and GPS preserved");
        }
        Check(Catalog.List(database, new DateOnly(2020, 1, 2), new DateOnly(2020, 1, 2)).Count == 1, "inclusive capture date range");
        var fallback = Catalog.List(database, new DateOnly(2021, 7, 2), new DateOnly(2021, 7, 2));
        Check(fallback.Count == 2 && fallback.All(row => row.DateSource == "file-modified-utc-estimated"), "missing or invalid EXIF uses explicit UTC estimate");
        Check(Catalog.List(database, limit: 1).Count == 1, "bounded query");
        Check(Catalog.List(database, root: root, offset: 1).Count == 4, "folder-filtered next page");
        Check(Catalog.List(database, root: Path.Combine(output, "other")).Count == 0, "other roots excluded");
        Expect<ArgumentOutOfRangeException>(() => Catalog.List(database, offset: -1), "negative offset rejected");
        var repeat = Catalog.Scan(root.ToUpperInvariant(), database);
        Check(repeat.Skipped == 3 && repeat.Written == 2 && Catalog.List(database).Count == 5, "idempotent roots and retryable errors");
        foreach (var pair in hashes) Check(pair.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pair.Key))), "source bytes unchanged");
        Expect<ArgumentException>(() => Catalog.Scan(root, Path.Combine(root, "unsafe.sqlite")), "database inside source rejected");
        Check(!File.Exists(Path.Combine(root, "unsafe.sqlite")), "no database written in source");
        Expect<DirectoryNotFoundException>(() => Catalog.Scan(Path.Combine(output, "offline"), database), "unavailable root rejected");
        Check(Catalog.List(database).Count == 5, "offline scan retains rows");
        Expect<ArgumentException>(() => Catalog.List(database, new DateOnly(2022, 1, 1), new DateOnly(2020, 1, 1)), "reversed dates rejected");
        var foreign = Path.Combine(output, "foreign.sqlite");
        using (var connection = new SqliteConnection($"Data Source={foreign};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE unrelated(value TEXT)";
            command.ExecuteNonQuery();
        }
        var foreignHash = SHA256.HashData(File.ReadAllBytes(foreign));
        Expect<IOException>(() => Catalog.Scan(root, foreign), "foreign database rejected");
        Check(foreignHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(foreign))), "foreign database unchanged");
        using (var image = new MagickImage(MagickColors.Green, 12, 13)) image.Write(png);
        Catalog.Scan(root, database);
        Check(Catalog.List(database).Single(row => row.RelativePath.EndsWith("fallback.png")).Width == 12
            && Catalog.List(database).Count == 5, "changed file updates without duplicates");
        Console.WriteLine("PASS: catalog self-test completed");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception("FAIL: " + message);
    }
}
