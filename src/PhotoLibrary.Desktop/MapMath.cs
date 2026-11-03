// 위경도를 지도 픽셀과 현재 화면에 필요한 타일 좌표로 변환합니다.
using System.Windows;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal readonly record struct MapTile(int Zoom, int X, int Y, double Left, double Top);

internal static class MapMath
{
    internal static Point World(GpsCoordinates location, int zoom)
    {
        var size = 256d * (1 << zoom);
        var latitude = Math.Clamp(location.Latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        return new Point((location.Longitude + 180) / 360 * size,
            Math.Clamp((1 - Math.Asinh(Math.Tan(latitude)) / Math.PI) / 2 * size, 0, size));
    }

    internal static GpsCoordinates Location(Point point, int zoom)
    {
        var size = 256d * (1 << zoom);
        var x = (point.X % size + size) % size;
        return new GpsCoordinates(Math.Atan(Math.Sinh(Math.PI * (1 - 2 * Math.Clamp(point.Y, 0, size) / size))) * 180 / Math.PI,
            x / size * 360 - 180);
    }

    internal static IReadOnlyList<MapTile> Visible(GpsCoordinates center, int zoom, double width, double height)
    {
        var world = World(center, zoom);
        var count = 1 << zoom;
        var left = world.X - width / 2;
        var top = world.Y - height / 2;
        var tiles = new List<MapTile>();
        for (var y = Math.Max(0, (int)Math.Floor(top / 256)); y <= Math.Min(count - 1, (int)Math.Floor((top + height - 1) / 256)); y++)
            for (var x = (int)Math.Floor(left / 256); x <= (int)Math.Floor((left + width - 1) / 256); x++)
                tiles.Add(new MapTile(zoom, (x % count + count) % count, y, x * 256 - left, y * 256 - top));
        return tiles;
    }
}
