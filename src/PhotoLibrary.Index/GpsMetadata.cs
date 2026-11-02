// 원본 EXIF JSON의 GPS 유리수와 방향을 검증해 십진 좌표를 읽습니다.
using System.Text.Json;

namespace PhotoLibrary.Index;

public sealed record GpsCoordinates(double Latitude, double Longitude);
public sealed record GpsReadResult(GpsCoordinates? Coordinates, string? Error);

public static class GpsMetadata
{
    public static GpsReadResult Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(null, "GPS_INVALID_JSON");
            var names = new[] { "GPSLatitude", "GPSLongitude", "GPSLatitudeRef", "GPSLongitudeRef" };
            var present = names.Count(name => root.TryGetProperty(name, out _));
            if (present == 0) return new(null, null);
            if (present != 4) return new(null, "GPS_INCOMPLETE");
            if (!TryDms(root.GetProperty("GPSLatitude"), 90, out var latitude)
                || !TryDms(root.GetProperty("GPSLongitude"), 180, out var longitude)
                || !TryDirection(root.GetProperty("GPSLatitudeRef"), "NS", out var northSouth)
                || !TryDirection(root.GetProperty("GPSLongitudeRef"), "EW", out var eastWest))
                return new(null, "GPS_INVALID_VALUE");
            if (northSouth == 'S') latitude = -latitude;
            if (eastWest == 'W') longitude = -longitude;
            return new(new GpsCoordinates(latitude == 0 ? 0 : latitude, longitude == 0 ? 0 : longitude), null);
        }
        catch (JsonException) { return new(null, "GPS_INVALID_JSON"); }
    }

    private static bool TryDms(JsonElement element, int maximum, out double value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 3) return false;
        var parts = new double[3];
        for (var index = 0; index < 3; index++)
        {
            var part = element[index];
            if (part.ValueKind != JsonValueKind.Object
                || !part.TryGetProperty("Numerator", out var numerator) || numerator.ValueKind != JsonValueKind.Number
                || !part.TryGetProperty("Denominator", out var denominator) || denominator.ValueKind != JsonValueKind.Number
                || !numerator.TryGetUInt32(out var n) || !denominator.TryGetUInt32(out var d) || d == 0) return false;
            parts[index] = (double)n / d;
        }
        if (parts[0] > maximum || parts[1] >= 60 || parts[2] >= 60) return false;
        value = parts[0] + parts[1] / 60 + parts[2] / 3600;
        return value <= maximum;
    }

    private static bool TryDirection(JsonElement element, string allowed, out char direction)
    {
        direction = default;
        if (element.ValueKind != JsonValueKind.String) return false;
        var text = element.GetString()!.Trim('\0', ' ', '\t', '\r', '\n').ToUpperInvariant();
        if (text.Length != 1 || !allowed.Contains(text[0])) return false;
        direction = text[0];
        return true;
    }
}
