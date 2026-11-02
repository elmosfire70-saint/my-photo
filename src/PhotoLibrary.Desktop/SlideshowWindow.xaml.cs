// 읽기 전용 사진 목록을 페이지 단위로 탐색하며 전체 화면과 슬라이드쇼 타이머 수명을 관리합니다.
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

public partial class SlideshowWindow : Window
{
    private const int PageSize = 200;
    private readonly string _database, _root;
    private readonly DateOnly? _from, _to;
    private readonly DispatcherTimer _timer = new();
    private IReadOnlyList<MediaRow> _page = [];
    private int _pageOffset = -1, _position;
    private bool _busy, _closed, _atEnd;
    internal bool IsPlaying { get; private set; }
    internal int IntervalSeconds { get; private set; }
    internal MediaRow? Current { get; private set; }
    internal int Position => _position;
    internal Task DisplayTask { get; private set; } = Task.CompletedTask;

    internal SlideshowWindow(string database, string root, DateOnly? from, DateOnly? to,
        int position, int seconds, bool autoPlay)
    {
        InitializeComponent();
        _database = database; _root = root; _from = from; _to = to; _position = position;
        IntervalSeconds = seconds;
        IntervalInput.Text = seconds.ToString(CultureInfo.InvariantCulture);
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        SetPlaying(autoPlay);
        _timer.Tick += (_, _) => DisplayTask = NavigateAsync(_position + 1, 1);
        Loaded += (_, _) => DisplayTask = NavigateAsync(position, 1);
        Closed += (_, _) => { _closed = true; SetPlaying(false); Photo.Source = null; };
    }

    internal static bool TrySeconds(string text, out int seconds) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds is >= 1 and <= 300;

    private void SetPlaying(bool playing)
    {
        IsPlaying = playing;
        _timer.Stop();
        PlayPause.Content = playing ? "일시정지 (Space)" : "재생 (Space)";
        if (playing && !_busy && !_closed && Current is not null) _timer.Start();
    }

    private async Task NavigateAsync(int target, int direction)
    {
        if (_busy || _closed) return;
        _busy = true;
        _timer.Stop();
        Previous.IsEnabled = Next.IsEnabled = false;
        var skipped = 0;
        Hint.Text = "사진 불러오는 중…";
        try
        {
            while (target >= 0 && !_closed)
            {
                var offset = target / PageSize * PageSize;
                if (offset != _pageOffset)
                {
                    // ponytail: 기존 OFFSET 조회를 재사용합니다. 대규모 뒤쪽 탐색은 키셋 조회로 보완합니다.
                    _page = await Task.Run(() => Catalog.List(_database, _from, _to, PageSize, _root, offset));
                    _pageOffset = offset;
                }
                if (_closed) return;
                var index = target - _pageOffset;
                if (index >= _page.Count) break;
                var row = _page[index];
                if (row.Error is not null || Catalog.IsVideo(row.RelativePath)) { skipped++; target += direction; continue; }
                try
                {
                    var bitmap = await Task.Run(() => PreviewLoader.Load(row.Root, row.RelativePath));
                    if (_closed) return;
                    Current = row;
                    _position = target;
                    _atEnd = false;
                    Photo.Source = bitmap;
                    Caption.Text = row.RelativePath;
                    Hint.Text = $"화면 맞춤 · 미리보기 긴 변 최대 2400px · {IntervalSeconds}초 간격 · Esc 복귀"
                        + (skipped > 0 ? $" · {skipped}개 동영상/오류/읽기 실패 건너뜀" : "");
                    return;
                }
                catch (Exception exception) when (!_closed && exception is IOException or UnauthorizedAccessException or ImageMagick.MagickException)
                { skipped++; target += direction; }
            }
            if (!_closed)
            {
                _atEnd = direction > 0;
                SetPlaying(false);
                Hint.Text = Current is null ? "표시할 사진이 없습니다. 동영상/오류/읽기 실패 항목은 제외합니다."
                    : _atEnd ? "마지막 사진입니다. 다시 재생하면 처음부터 시작합니다." : "첫 번째 사진입니다.";
            }
        }
        catch (Exception exception) { if (!_closed) { SetPlaying(false); Hint.Text = "사진 조회 실패 · " + exception.Message; } }
        finally
        {
            _busy = false;
            if (!_closed)
            {
                Previous.IsEnabled = Next.IsEnabled = Current is not null;
                if (IsPlaying && Current is not null) _timer.Start();
            }
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => DisplayTask = NavigateAsync(_position - 1, -1);
    private void Next_Click(object sender, RoutedEventArgs e) => DisplayTask = NavigateAsync(_position + 1, 1);
    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        SetPlaying(!IsPlaying);
        if (IsPlaying && !_busy && (_atEnd || Current is null)) DisplayTask = NavigateAsync(0, 1);
    }
    private void ApplyInterval_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySeconds(IntervalInput.Text, out var seconds))
        { Hint.Text = $"1~300초의 정수를 입력하세요. 기존 {IntervalSeconds}초 설정은 유지됩니다."; IntervalInput.Focus(); return; }
        IntervalSeconds = seconds;
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        SetPlaying(IsPlaying);
        Hint.Text = $"{seconds}초 간격 적용 · Space 재생/일시정지 · Esc 복귀";
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F11) { e.Handled = true; Close(); return; }
        if (e.OriginalSource is TextBox)
        { if (e.Key == Key.Enter) { e.Handled = true; ApplyInterval_Click(this, e); } return; }
        switch (e.Key)
        {
            case Key.Space: e.Handled = true; PlayPause_Click(this, e); break;
            case Key.Left: e.Handled = true; Previous_Click(this, e); break;
            case Key.Right: e.Handled = true; Next_Click(this, e); break;
        }
    }
}
