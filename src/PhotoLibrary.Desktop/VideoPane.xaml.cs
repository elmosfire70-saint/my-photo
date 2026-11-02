// 앱에 포함한 VLC로 로컬 영상의 준비·재생·정지와 자원 수명을 관리합니다.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

public partial class VideoPane : UserControl, IDisposable
{
    private LibVLC? _engine;
    private MediaPlayer? _player;
    private readonly DispatcherTimer _timer;
    private int _generation;
    private bool _updating, _disposed;
    internal MediaPlayer? Player => _player;
    internal string EngineVersion => _engine?.Version ?? "not initialized";

    public VideoPane()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdatePlayback();
    }

    internal async Task LoadAsync(string root, string relativePath)
    {
        var path = Catalog.ResolveMediaPath(root, relativePath);
        if (!Catalog.IsVideo(path)) throw new IOException("MOV/MP4 로컬 파일만 재생합니다.");
        Reset();
        var generation = _generation;
        if (_engine is null)
        {
            Core.Initialize(Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"));
            _engine = new LibVLC("--no-video-title-show", "--no-osd", "--no-metadata-network-access", "--no-sub-autodetect-file");
            _player = new MediaPlayer(_engine) { Volume = (int)Volume.Value };
            Surface.MediaPlayer = _player;
        }
        Information.Text = "로컬 영상 정보 확인 중…";
        using var media = new Media(_engine, path, FromType.FromPath);
        var status = await media.Parse(MediaParseOptions.ParseLocal, 5000);
        if (_disposed || generation != _generation) return;
        if (status != MediaParsedStatus.Done || !media.Tracks.Any(track => track.TrackType == TrackType.Video))
            throw new IOException("동영상 스트림을 읽을 수 없습니다. 손상·지원하지 않는 형식일 수 있습니다.");
        _player!.Media = media;
        PlayButton.IsEnabled = PauseButton.IsEnabled = StopButton.IsEnabled = true;
        Information.Text = $"재생 준비 · LibVLC {EngineVersion} · 재생 버튼을 누르세요.";
        Clock.Text = "00:00 / " + FormatTime(media.Duration);
        _timer.Start();
    }

    internal void Reset()
    {
        _generation++;
        _timer?.Stop();
        if (_player is not null)
        {
            _player.Stop();
            _player.Media = null;
        }
        PlayButton.IsEnabled = PauseButton.IsEnabled = StopButton.IsEnabled = Seek.IsEnabled = false;
        _updating = true;
        Seek.Value = 0;
        _updating = false;
        Information.Text = "동영상을 선택하세요.";
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (_player.State == VLCState.Paused) _player.SetPause(false);
        else if (!_player.Play()) Information.Text = "재생을 시작하지 못했습니다.";
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => _player?.SetPause(true);
    private void Stop_Click(object sender, RoutedEventArgs e) { _player?.Stop(); Clock.Text = "정지됨"; }
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player is not null) _player.Volume = (int)e.NewValue;
    }
    private void Seek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updating && _player?.IsSeekable == true) _player.Position = (float)e.NewValue;
    }
    private void UpdatePlayback()
    {
        if (_player is null || _disposed) return;
        _updating = true;
        Seek.IsEnabled = _player.IsSeekable;
        Seek.Value = Math.Clamp(_player.Position, 0, 1);
        _updating = false;
        Clock.Text = FormatTime(_player.Time) + " / " + FormatTime(_player.Length);
        Information.Text = _player.State switch
        {
            VLCState.Error => "재생 실패 · 이 파일의 코덱·손상 상태를 확인하세요.",
            VLCState.Playing => "재생 중 · HDR/Dolby Vision 색상은 기기·디스플레이에 따라 다릅니다.",
            VLCState.Paused => "일시정지", VLCState.Ended => "재생 완료", VLCState.Stopped => "정지됨",
            _ => Information.Text
        };
    }
    private static string FormatTime(long milliseconds) => TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)).ToString(@"hh\:mm\:ss");
    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
        Surface.MediaPlayer = null;
        _player?.Dispose();
        _engine?.Dispose();
    }
}
