// 공개 도구로 생성한 시험 미디어의 읽기와 안전한 메타데이터 복원을 검증합니다.
using ImageMagick;
using System.Security.Cryptography;

if (args.Length != 1) throw new ArgumentException("Usage: MediaPreflight NEW_OUTPUT_DIRECTORY");
var output = Path.GetFullPath(args[0]);
var artifactRoot = Path.GetFullPath("test-artifacts");
if (!output.StartsWith(artifactRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new IOException("Output must be a new directory under test-artifacts in the project root.");
for (var parent = Directory.GetParent(output); parent is not null; parent = parent.Parent)
{
    if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
        throw new IOException("Output ancestors must not be symbolic links or junctions.");
}
if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
Directory.CreateDirectory(output);
var sample = Path.Combine(output, "sample.jpg");
using (var image = new MagickImage(MagickColors.CornflowerBlue, 64, 48)) image.Write(sample);
var dimensions = MediaProbe.Inspect(sample);
if (dimensions != (64u, 48u)) throw new Exception("FAIL: JPEG dimensions");
Console.WriteLine("PASS: JPEG dimensions");
var working = Path.Combine(output, "working.jpg");
File.Copy(sample, working);
var originalHash = SHA256.HashData(File.ReadAllBytes(sample));
var backup = Path.Combine(output, "backup.jpg");
File.Copy(working, backup);
try
{
    MediaProbe.WriteTestMetadata(working);
    using var reread = new MagickImage(working);
    var profile = reread.GetExifProfile() ?? throw new Exception("FAIL: missing EXIF");
    if (profile.GetValue(ExifTag.DateTimeOriginal)?.Value != "2020:01:02 03:04:05"
        || profile.GetValue(ExifTag.GPSLatitudeRef)?.Value != "N"
        || profile.GetValue(ExifTag.GPSLongitudeRef)?.Value != "E"
        || profile.GetValue(ExifTag.GPSLatitude)?.Value is not { } latitude
        || !latitude.SequenceEqual(new[] { new Rational(32u), new Rational(0u), new Rational(0u) })
        || profile.GetValue(ExifTag.GPSLongitude)?.Value is not { } longitude
        || !longitude.SequenceEqual(new[] { new Rational(126u), new Rational(0u), new Rational(0u) }))
        throw new Exception("FAIL: date/GPS reread");
    Console.WriteLine("PASS: JPEG date/GPS write and reread");
}
finally
{
    File.Copy(backup, working, overwrite: true);
}
if (!originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(working)))
    || !originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(sample))))
    throw new Exception("FAIL: restore/source hash");
Console.WriteLine("PASS: restored SHA256 and untouched source");
foreach (var extension in new[] { "jpg", "png", "bmp", "gif", "tiff", "webp", "heic", "avif" })
{
    var path = Path.Combine(output, "format." + extension);
    try
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, 64, 48);
        image.Write(path);
    }
    catch (MagickMissingDelegateErrorException) when (extension is "heic" or "avif")
    {
        Console.WriteLine("UNVERIFIED: " + extension + " has no synthetic encoder; real-file decode needs a separate fixture");
        continue;
    }
    if (MediaProbe.Inspect(path) != (64u, 48u)) throw new Exception("FAIL: " + extension);
    var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
    var preview = Path.Combine(output, extension + "-preview.jpg");
    MediaProbe.Preview(path, preview);
    if (MediaProbe.Inspect(preview) != (32u, 24u)) throw new Exception("FAIL: preview " + extension);
    if (!sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))))
        throw new Exception("FAIL: preview changed source " + extension);
    Console.WriteLine("PASS: " + extension + " synthetic decode and preview");
}
var corrupt = Path.Combine(output, "corrupt.jpg");
File.WriteAllText(corrupt, "not an image");
try
{
    MediaProbe.Inspect(corrupt);
    throw new Exception("FAIL: corrupt file accepted");
}
catch (MagickException)
{
    Console.WriteLine("PASS: corrupt image rejected");
}
