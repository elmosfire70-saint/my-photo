// 사진 선택·지도 이동/확대·타일 취소와 늦은 결과를 관리하며 원본 좌표를 외부에 보내지 않습니다.
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

public partial class MapPane : UserControl, IDisposable
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private MapTileCache _cache;
    private GpsCoordinates _center = new(36, 127.5);
    private GpsCoordinates? _point;
    private int _zoom = 5, _version;
    private bool _disposed;
    private CancellationTokenSource? _work;
    private Point? _drag;
    private Point _dragCenter;
    internal bool HasMarker => _point is not null;
    internal int ZoomLevel => _zoom;
    internal int VisibleTileCount { get; private set; }
    internal Task RenderTask { get; private set; } = Task.CompletedTask;

    public MapPane()
    {
        InitializeComponent();
        _cache = new MapTileCache(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyPhoto", "map-tiles-v1"), Client);
        Loaded += (_, _) => Queue();
        _delay.Tick += (_, _) => { _delay.Stop(); RenderTask = RenderAsync(); };
    }

    internal void Configure(bool online, string? cacheRoot = null, HttpClient? client = null)
    {
        if (cacheRoot is not null) _cache = new MapTileCache(cacheRoot, client ?? Client);
        Online.IsChecked = online;
        Queue();
    }

    internal void SetPhoto(MediaRow? row)
    {
        _point = null;
        var label = "사진 위치 · 사진을 선택하세요.";
        if (row is not null)
        {
            var gps = GpsMetadata.Read(row.ExifJson);
            label = Catalog.IsVideo(row.RelativePath) ? "영상 GPS · 아직 미추출"
                : row.Error is not null ? "사진 읽기 오류 · 위치 미확정"
                : gps.Error is not null ? "GPS 누락/형식 오류 · 위치 미확정" : "GPS 정보 없음 · 위치 추정하지 않음";
            if (!Catalog.IsVideo(row.RelativePath) && row.Error is null && gps.Coordinates is { } coordinates)
            {
                using var document = JsonDocument.Parse(row.ExifJson);
                var known = document.RootElement.TryGetProperty("GPSMapDatum", out var datum);
                var wgs84 = known && datum.ValueKind == JsonValueKind.String
                    && datum.GetString()!.Replace("-", "").Replace(" ", "").Trim('\0').Equals("WGS84", StringComparison.OrdinalIgnoreCase);
                if (known && !wgs84) label = "측지기준 미지원 · 지도 위치 미확정";
                else if (Math.Abs(coordinates.Latitude) > 85.05112878) label = "지도 위도 표현 범위 밖 · 원본 GPS는 메타데이터에서 확인";
                else
                {
                    _point = coordinates;
                    label = FormattableString.Invariant($"GPS {coordinates.Latitude:F5}, {coordinates.Longitude:F5} · {(known ? "원본 좌표" : "참고 위치(기준 미확인)")}");
                }
            }
        }
        LocationLabel.Text = label;
        LocationLabel.ToolTip = label;
        _center = _point ?? new GpsCoordinates(36, 127.5);
        _zoom = _point is null ? 5 : 13;
        Queue();
    }

    private void Queue()
    {
        if (_disposed) return;
        _drag = null;
        if (Surface.IsMouseCaptured) Surface.ReleaseMouseCapture();
        _version++;
        _work?.Cancel();
        _delay.Stop();
        Layer.Children.Clear();
        VisibleTileCount = 0;
        if (IsLoaded) _delay.Start();
    }

    internal Task RefreshNowAsync() { _delay.Stop(); return RenderTask = RenderAsync(); }

    private async Task RenderAsync()
    {
        if (_disposed || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return;
        var version = ++_version;
        _work?.Cancel(); _work?.Dispose(); _work = new CancellationTokenSource();
        var cancellation = _work.Token;
        var online = Online.IsChecked == true;
        var cache = _cache;
        Layer.Children.Clear(); Layer.RenderTransform = Transform.Identity;
        VisibleTileCount = 0;
        var tiles = MapMath.Visible(_center, _zoom, Surface.ActualWidth, Surface.ActualHeight);
        if (_point is { } point)
        {
            var center = MapMath.World(_center, _zoom); var marker = MapMath.World(point, _zoom);
            var pin = new System.Windows.Shapes.Path { Data = Geometry.Parse("M10,0 C-3,0 -3,16 10,28 C23,16 23,0 10,0 Z"),
                Fill = Brushes.Crimson, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
            Canvas.SetLeft(pin, Surface.ActualWidth / 2 + Math.IEEERemainder(marker.X - center.X, 256d * (1 << _zoom)) - 10);
            Canvas.SetTop(pin, Surface.ActualHeight / 2 + marker.Y - center.Y - 28);
            Panel.SetZIndex(pin, 1); Layer.Children.Add(pin);
        }
        TileStatus.Text = online ? "지도 불러오는 중…" : "온라인 꺼짐 · 기존 캐시만 표시";
        var failed = 0;
        // 현재 화면의 중복 타일만 합칩니다. 화면 밖/다른 확대 단계는 미리 받지 않습니다.
        await Task.WhenAll(tiles.GroupBy(tile => (tile.Zoom, tile.X, tile.Y)).Select(async group =>
        {
            try
            {
                var bitmap = await cache.GetAsync(group.Key.Zoom, group.Key.X, group.Key.Y, online, cancellation);
                if (_disposed || version != _version) return;
                foreach (var tile in group)
                {
                    var image = new Image { Source = bitmap, Width = 256, Height = 256, IsHitTestVisible = false };
                    Canvas.SetLeft(image, tile.Left); Canvas.SetTop(image, tile.Top);
                    Layer.Children.Add(image); VisibleTileCount++;
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException or NotSupportedException or FormatException)
            { if (!cancellation.IsCancellationRequested) failed++; }
        }));
        if (!_disposed && version == _version)
            TileStatus.Text = failed == 0 ? $"지도 {_zoom}단계 · 드래그 이동 / 휠 확대"
                : online ? "일부 지도 연결 실패 · 사진 보기는 계속할 수 있습니다." : "온라인 꺼짐 · 일부 지도 캐시 없음";
    }

    private void ChangeZoom(int delta) { _zoom = Math.Clamp(_zoom + delta, 2, 18); Queue(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ChangeZoom(1);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ChangeZoom(-1);
    private void Online_Changed(object sender, RoutedEventArgs e) => Queue();
    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => Queue();
    private void Surface_MouseWheel(object sender, MouseWheelEventArgs e) { ChangeZoom(e.Delta > 0 ? 1 : -1); e.Handled = true; }
    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    { _drag = e.GetPosition(Surface); _dragCenter = MapMath.World(_center, _zoom); Surface.CaptureMouse(); e.Handled = true; }
    private void Surface_MouseMove(object sender, MouseEventArgs e)
    { if (_drag is { } start && e.LeftButton == MouseButtonState.Pressed) { var delta = e.GetPosition(Surface) - start; Layer.RenderTransform = new TranslateTransform(delta.X, delta.Y); } }
    private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not { } start) return;
        var delta = e.GetPosition(Surface) - start;
        _center = MapMath.Location(new Point(_dragCenter.X - delta.X, _dragCenter.Y - delta.Y), _zoom);
        _drag = null; Surface.ReleaseMouseCapture(); Queue();
    }
    private void Attribution_Click(object sender, RequestNavigateEventArgs e)
    { Process.Start(new ProcessStartInfo("https://www.openstreetmap.org/copyright") { UseShellExecute = true }); e.Handled = true; }
    public void Dispose() { _disposed = true; _version++; _delay.Stop(); _work?.Cancel(); _work?.Dispose(); Layer.Children.Clear(); _point = null; VisibleTileCount = 0; }
}
