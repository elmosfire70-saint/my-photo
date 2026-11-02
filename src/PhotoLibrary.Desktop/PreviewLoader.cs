// 원본을 수정하거나 잠금을 남기지 않고 크기가 제한된 첫 프레임 미리보기를 만듭니다.
using System.IO;
using System.Windows.Media.Imaging;
using ImageMagick;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal static class PreviewLoader
{
    internal static BitmapSource Load(string root, string relativePath)
    {
        var path = Catalog.ResolveMediaPath(root, relativePath);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using (var header = new MagickImage())
        {
            header.Ping(stream);
            if ((ulong)header.Width * header.Height > 50_000_000)
                throw new IOException("5천만 픽셀을 넘는 사진의 미리보기는 아직 지원하지 않습니다.");
        }
        stream.Position = 0;
        using var image = new MagickImage(stream, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        image.AutoOrient();
        image.Thumbnail(new MagickGeometry(2400, 2400) { Greater = true });
        using var png = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = png;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
