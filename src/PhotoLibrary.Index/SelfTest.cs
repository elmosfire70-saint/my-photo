// 합성 이미지로 DB 수집·중단/재개·날짜 검색과 원본 불변성을 검증합니다.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Check(GpsMetadata.Read(captured.ExifJson).Error == "GPS_INCOMPLETE", "partial GPS is not guessed");
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
        Check(Catalog.Months(database, root).Sum(month => month.Count) == 5, "timeline counts entire root");
        var timelineRoot = Path.Combine(output, "timeline ä");
        Directory.CreateDirectory(timelineRoot);
        foreach (var sample in new[] { ("leap-a", "2020:02:29 23:59:59"), ("leap-b", "2020:02:01 00:00:00"),
            ("dec", "2020:12:31 23:59:59"), ("jan", "2021:01:01 00:00:00") })
        {
            using var image = new MagickImage(MagickColors.Blue, 8, 6);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, sample.Item2);
            image.SetProfile(exif);
            image.Write(Path.Combine(timelineRoot, sample.Item1 + ".jpg"));
        }
        using (var image = new MagickImage(MagickColors.Red, 8, 6)) image.Write(Path.Combine(timelineRoot, "estimated.png"));
        File.WriteAllText(Path.Combine(timelineRoot, "pending.mp4"), "synthetic catalog-only placeholder");
        File.WriteAllText(Path.Combine(timelineRoot, "unverified.cr2"), "synthetic raw placeholder");
        foreach (var name in new[] { "estimated.png", "pending.mp4", "unverified.cr2" })
            File.SetLastWriteTimeUtc(Path.Combine(timelineRoot, name), new DateTime(2022, 3, 5, 0, 0, 0, DateTimeKind.Utc));
        var timelineHashes = Directory.GetFiles(timelineRoot).ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        Catalog.Scan(timelineRoot, database);
        var databaseHash = SHA256.HashData(File.ReadAllBytes(database));
        var months = Catalog.Months(database, timelineRoot.ToUpperInvariant());
        Check(months.Select(month => month.Month).SequenceEqual(new[] { "2022-03", "2021-01", "2020-12", "2020-02" }), "descending calendar months");
        Check(months[0] == new MonthSummary("2022-03", 3, 3, 1) && months[3] == new MonthSummary("2020-02", 2, 0, 0),
            "timeline separates estimated UTC and errors");
        Check(months.Sum(month => month.Count) == 7 && Catalog.Months(database, root).Sum(month => month.Count) == 5, "timeline roots isolated");
        Check(Catalog.Months(database, Path.Combine(output, "missing-root")).Count == 0, "empty timeline");
        Check(Catalog.List(database, new DateOnly(2020, 2, 1), new DateOnly(2020, 2, 29), root: timelineRoot).Count == 2, "leap month bounds");
        Check(Catalog.List(database, new DateOnly(2020, 12, 1), new DateOnly(2020, 12, 31), root: timelineRoot).Count == 1, "December bounds");
        Check(Catalog.List(database, new DateOnly(2021, 1, 1), new DateOnly(2021, 12, 31), root: timelineRoot).Count == 1, "year bounds exclude previous December");
        Check(databaseHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(database))), "timeline queries do not mutate database");
        Check(timelineHashes.All(pair => pair.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pair.Key)))), "timeline sources unchanged");
        CheckGps();
        CheckBackup(database, output);
        Console.WriteLine("PASS: catalog self-test completed");
    }

    private static void CheckBackup(string database, string output)
    {
        var sourceHash = SHA256.HashData(File.ReadAllBytes(database));
        var backup = Path.Combine(output, "backup.sqlite");
        Catalog.Backup(database, backup);
        Check(Catalog.List(database).SequenceEqual(Catalog.List(backup)), "backup reopens with identical catalog rows");
        Check(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(database))), "backup does not modify source database");
        var backupHash = SHA256.HashData(File.ReadAllBytes(backup));
        Expect<IOException>(() => Catalog.Backup(database, backup), "existing backup refused");
        Expect<IOException>(() => Catalog.Backup(database, database), "source overwrite refused");
        Check(backupHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backup))), "existing backup preserved");
        Expect<DirectoryNotFoundException>(() => Catalog.Backup(database, Path.Combine(output, "missing", "backup.sqlite")), "missing backup directory rejected");
        var invalid = Path.Combine(output, "corrupt.sqlite"); File.WriteAllText(invalid, "not a database");
        var failed = Path.Combine(output, "failed-backup.sqlite");
        Expect<SqliteException>(() => Catalog.Backup(invalid, failed), "corrupt source rejected");
        Check(!File.Exists(failed) && !Directory.GetFiles(output, ".photo-backup-*").Any(), "failure leaves no published or temporary backup");
        var inconsistent = Path.Combine(output, "inconsistent.sqlite");
        Catalog.Backup(database, inconsistent);
        using (var broken = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = inconsistent, Pooling = false }.ToString()))
        {
            broken.Open();
            using var command = broken.CreateCommand();
            command.CommandText = "CREATE TABLE invalid_fixture(value INTEGER CHECK(value>0)); PRAGMA ignore_check_constraints=ON; INSERT INTO invalid_fixture VALUES(-1);";
            command.ExecuteNonQuery();
        }
        Expect<IOException>(() => Catalog.Backup(inconsistent, failed), "integrity failure prevents publication");
        Check(!File.Exists(failed) && !Directory.GetFiles(output, ".photo-backup-*").Any(), "integrity failure cleans owned temporary files");
        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            writer.Open();
            writer.CreateCollation("WINPATH", (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left, right));
            using var command = writer.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE media SET offset_text='+01:23';";
            command.ExecuteNonQuery();
            Check(new FileInfo(database + "-wal").Length > 0, "committed changes remain in WAL during backup");
            var walBackup = Path.Combine(output, "wal-backup.sqlite");
            Catalog.Backup(database, walBackup);
            Check(Catalog.List(walBackup).All(row => row.Offset == "+01:23"), "backup includes committed WAL changes");
        }
    }

    private static void CheckGps()
    {
        static string Json(uint[] latitude, uint[] longitude, string northSouth = "N", string eastWest = "E", uint denominator = 1) =>
            JsonSerializer.Serialize(new
            {
                GPSLatitude = latitude.Select(n => new { Numerator = n, Denominator = denominator }),
                GPSLongitude = longitude.Select(n => new { Numerator = n, Denominator = denominator }),
                GPSLatitudeRef = northSouth, GPSLongitudeRef = eastWest
            });
        var valid = Json([37, 30, 0], [126, 15, 0]);
        Check(GpsMetadata.Read(valid).Coordinates == new GpsCoordinates(37.5, 126.25), "GPS rational conversion");
        Check(GpsMetadata.Read(Json([37, 30, 0], [126, 15, 0], denominator: 2)).Coordinates == new GpsCoordinates(18.75, 63.125), "GPS fractional rationals");
        Check(GpsMetadata.Read(Json([37, 30, 0], [126, 15, 0], "S", "W")).Coordinates == new GpsCoordinates(-37.5, -126.25), "GPS south west signs");
        Check(GpsMetadata.Read(Json([1, 0, 0], [2, 0, 0], " s\0 ", "w")).Coordinates == new GpsCoordinates(-1, -2), "GPS reference padding and case");
        Check(GpsMetadata.Read(Json([0, 0, 0], [0, 0, 0])).Coordinates == new GpsCoordinates(0, 0), "GPS zero is valid");
        Check(GpsMetadata.Read(Json([90, 0, 0], [180, 0, 0])).Coordinates == new GpsCoordinates(90, 180), "GPS poles and antimeridian");
        foreach (var invalid in new[]
        {
            Json([90, 1, 0], [180, 0, 0]), Json([0, 0, 0], [180, 0, 1]),
            Json([1, 60, 0], [2, 0, 0]), Json([1, 0, 60], [2, 0, 0]),
            Json([1, 0], [2, 0, 0]), Json([1, 0, 0], [2, 0, 0], denominator: 0),
            Json([1, 0, 0], [2, 0, 0], "X", "E"), Json([1, 0, 0], [2, 0, 0], "", "E")
        }) Check(GpsMetadata.Read(invalid).Error == "GPS_INVALID_VALUE", "GPS invalid value rejected");
        var negative = JsonNode.Parse(valid)!;
        negative["GPSLatitude"]![0]!["Numerator"] = -1;
        Check(GpsMetadata.Read(negative.ToJsonString()).Error == "GPS_INVALID_VALUE", "GPS negative rational rejected");
        var wrongType = JsonNode.Parse(valid)!;
        wrongType["GPSLongitude"] = "126.25";
        Check(GpsMetadata.Read(wrongType.ToJsonString()).Error == "GPS_INVALID_VALUE", "GPS wrong type rejected");
        var partial = JsonNode.Parse(valid)!.AsObject();
        partial.Remove("GPSLatitudeRef");
        Check(GpsMetadata.Read(partial.ToJsonString()).Error == "GPS_INCOMPLETE", "GPS missing direction not guessed");
        Check(GpsMetadata.Read("{}") == new GpsReadResult(null, null), "GPS absent remains unknown");
        Check(GpsMetadata.Read("{").Error == "GPS_INVALID_JSON" && GpsMetadata.Read("[]").Error == "GPS_INVALID_JSON", "GPS malformed JSON rejected");
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
