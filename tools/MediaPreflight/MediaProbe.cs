// 합성 시험 이미지의 읽기·미리보기·고정 시험 태그 쓰기를 검증합니다.
using ImageMagick;

internal static class MediaProbe
{
    internal static void Preview(string path, string destination)
    {
        using var image = new MagickImage(path);
        image.Thumbnail(32, 32);
        image.Write(destination);
    }

    internal static void WriteTestMetadata(string path)
    {
        using var image = new MagickImage(path);
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.DateTimeOriginal, "2020:01:02 03:04:05");
        profile.SetValue(ExifTag.GPSLatitudeRef, "N");
        profile.SetValue(ExifTag.GPSLongitudeRef, "E");
        profile.SetValue(ExifTag.GPSLatitude, new[] { new Rational(32u), new Rational(0u), new Rational(0u) });
        profile.SetValue(ExifTag.GPSLongitude, new[] { new Rational(126u), new Rational(0u), new Rational(0u) });
        image.SetProfile(profile);
        image.Write(path);
    }

    internal static (uint Width, uint Height) Inspect(string path)
    {
        using var image = new MagickImage(path);
        return (image.Width, image.Height);
    }
}
