// 공식 ExifTool의 합성 JPEG 태그 편집과 픽셀 보존·복원을 시험합니다.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;

internal static class ExifToolCheck
{
    internal static async Task RunAsync(string output, string perl, string script)
    {
        if ((await ExecuteAsync(perl, script, "-ver")).Trim() != "13.60")
            throw new Exception("FAIL: expected ExifTool 13.60");
        try
        {
            await ExecuteAsync(perl, script, "-j", "--", Path.Combine(output, "missing.jpg"));
            throw new Exception("FAIL: missing ExifTool input accepted");
        }
        catch (IOException)
        {
            Console.WriteLine("PASS: ExifTool nonzero exit rejected");
        }
        var source = Path.Combine(output, "sample.jpg");
        var working = Path.Combine(output, "exiftool-시험 복사본.jpg");
        var backup = Path.Combine(output, "exiftool-backup.jpg");
        File.Copy(source, working);
        File.Copy(working, backup);
        var originalHash = SHA256.HashData(File.ReadAllBytes(source));
        using var original = new MagickImage(source);
        var pixels = original.ToByteArray(MagickFormat.Rgb);
        var imageDataHash = await ImageHashAsync(perl, script, source);
        try
        {
            await ExecuteAsync(perl, script, "-overwrite_original", "-DateTimeOriginal=2020:01:02 03:04:05",
                "-GPSLatitude=32.5", "-GPSLatitudeRef=S", "-GPSLongitude=126.25", "-GPSLongitudeRef=W",
                "--", working);
            var json = await ExecuteAsync(perl, script, "-j", "-n", "-G1", "-DateTimeOriginal",
                "-GPSLatitude", "-GPSLatitudeRef", "-GPSLongitude", "-GPSLongitudeRef", "--", working);
            using var document = JsonDocument.Parse(json);
            var tags = document.RootElement[0];
            if (tags.GetProperty("ExifIFD:DateTimeOriginal").GetString() != "2020:01:02 03:04:05"
                || tags.GetProperty("GPS:GPSLatitudeRef").GetString() != "S"
                || tags.GetProperty("GPS:GPSLongitudeRef").GetString() != "W"
                || Math.Abs(tags.GetProperty("GPS:GPSLatitude").GetDouble() - 32.5) > 0.000001
                || Math.Abs(tags.GetProperty("GPS:GPSLongitude").GetDouble() - 126.25) > 0.000001)
                throw new Exception("FAIL: ExifTool JSON date/GPS reread");
            using var edited = new MagickImage(working);
            if (edited.Width != original.Width || edited.Height != original.Height
                || !pixels.SequenceEqual(edited.ToByteArray(MagickFormat.Rgb)))
                throw new Exception("FAIL: ExifTool changed decoded pixels");
            if (imageDataHash != await ImageHashAsync(perl, script, working))
                throw new Exception("FAIL: ExifTool changed image data hash");
            Console.WriteLine("PASS: ExifTool JSON date/GPS, decoded pixels and image data SHA256 preserved");
        }
        finally
        {
            File.Copy(backup, working, overwrite: true);
        }
        if (!originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(working)))
            || !originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))
            throw new Exception("FAIL: ExifTool restore/source hash");
        Console.WriteLine("PASS: ExifTool restored SHA256 and untouched source");
    }

    private static async Task<string> ImageHashAsync(string perl, string script, string path)
    {
        var hash = (await ExecuteAsync(perl, script, "-s3", "-api", "ImageHashType=SHA256", "-ImageDataHash", "--", path)).Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new Exception("FAIL: missing image data SHA256");
        return hash;
    }

    private static async Task<string> ExecuteAsync(string perl, string script, params string[] arguments)
    {
        var start = new ProcessStartInfo(perl)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-config");
        start.ArgumentList.Add("");
        start.ArgumentList.Add("-charset");
        start.ArgumentList.Add("filename=UTF8");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start ExifTool.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("ExifTool exceeded 30 seconds.");
        }
        var result = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
            throw new IOException($"ExifTool exit {process.ExitCode}: {error}");
        return result;
    }
}
