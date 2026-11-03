// 읽기 전용 수집·필터·페이지와 비동기 미리보기의 화면 상태를 연결합니다.
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal sealed record PhotoItem(MediaRow Record)
{
    public string DateLabel => $"{Record.DateText[..19].Replace('T', ' ')} · {(Record.DateSource == "exif-original-wall-clock" ? "촬영일" : "추정 UTC")}";
    public string StateLabel => Record.Error switch { null => Catalog.IsVideo(Record.RelativePath) ? "동영상" : "정상", "UNVERIFIED_FORMAT" => "미검증 형식", _ => "읽기 오류" };
}

public partial class MainWindow : Window
{
    private const int PageSize = 200;
    private string _database;
    private string? _root;
    private int _offset, _previewVersion, _queryVersion;
    private DateOnly? _listedFrom, _listedTo;
    private CancellationTokenSource? _scan;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _previewGate = new(1);
    private bool _closed, _closeAfterScan;
    private bool _updatingTimeline;
    private string? _timelineKey = "all";
    private sealed record TimelineChoice(string Key, DateTime? From, DateTime? To);
    private Point? _dragStart;
    private double _dragHorizontal, _dragVertical;
    internal Task PreviewTask { get; private set; } = Task.CompletedTask;
    internal Task QueryTask => _queryTask;
    private Task _queryTask = Task.CompletedTask;

    public MainWindow(string database, bool onlineMaps = false)
    {
        InitializeComponent();
        MapPanel.Configure(onlineMaps);
        _database = Path.GetFullPath(database);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _timer.Tick += async (_, _) => { if (_queryTask.IsCompleted) await RefreshRowsAsync(); };
        Closing += OnClosing;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F11) { e.Handled = true; CreateSlideshow(false)?.ShowDialog(); } };
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "기존 사진 폴더 선택", Multiselect = false, AddToRecent = false };
        if (picker.ShowDialog(this) == true) await ScanFolderAsync(picker.FolderName);
    }

    private void ChooseDatabase_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Title = "사진함 DB 위치", FileName = "catalog.sqlite", Filter = "사진함 DB|*.sqlite", AddToRecent = false, OverwritePrompt = false };
        if (picker.ShowDialog(this) == true)
        {
            _database = Path.GetFullPath(picker.FileName);
            Status.Text = "DB 위치를 선택했습니다. 폴더를 선택하거나 다시 수집하세요.";
            PhotoList.ItemsSource = null;
            ResetTimeline();
        }
    }

    internal async Task ScanFolderAsync(string root)
    {
        if (_scan is not null) return;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _offset = 0;
        PhotoList.ItemsSource = null;
        ResetTimeline();
        FolderLabel.Text = _root;
        FromDate.SelectedDate = ToDate.SelectedDate = null;
        ChooseFolder.IsEnabled = ChooseDatabase.IsEnabled = Rescan.IsEnabled = false;
        CancelScan.IsEnabled = true;
        _scan = new CancellationTokenSource();
        var cancellation = _scan.Token;
        _timer.Start();
        Status.Text = "수집 중 · 원본 파일을 수정하지 않습니다.";
        try
        {
            var result = await Task.Run(() => Catalog.Scan(_root, _database, cancellation));
            Status.Text = $"수집 완료 · 저장 {result.Written} · 재사용 {result.Skipped} · 오류/미검증 {result.Errors}";
        }
        catch (OperationCanceledException) { Status.Text = "수집 취소 · 다시 수집하면 저장한 항목을 재사용합니다."; }
        catch (Exception exception) { Status.Text = "수집 실패 · " + exception.Message; }
        finally
        {
            _timer.Stop();
            _scan.Dispose();
            _scan = null;
            ChooseFolder.IsEnabled = ChooseDatabase.IsEnabled = Rescan.IsEnabled = true;
            CancelScan.IsEnabled = false;
            await RefreshRowsAsync(refreshTimeline: true);
            if (_closeAfterScan) Close();
        }
    }

    internal Task RefreshRowsAsync(bool refreshTimeline = false)
    {
        _queryTask = RefreshAfterAsync(_queryTask, ++_queryVersion, refreshTimeline);
        return _queryTask;
    }

    private async Task RefreshAfterAsync(Task prior, int version, bool refreshTimeline)
    {
        await prior;
        if (_closed || _root is null || !File.Exists(_database)) return;
        var root = _root;
        var database = _database;
        var offset = _offset;
        var from = FromDate.SelectedDate is { } first ? DateOnly.FromDateTime(first) : (DateOnly?)null;
        var to = ToDate.SelectedDate is { } last ? DateOnly.FromDateTime(last) : (DateOnly?)null;
        try
        {
            var result = await Task.Run(() => (
                Rows: Catalog.List(database, from, to, PageSize + 1, root, offset),
                Months: refreshTimeline ? Catalog.Months(database, root) : null));
            if (_closed || root != _root || database != _database) return;
            // 타임라인은 기간/페이지와 무관하므로 오래된 목록 요청에서도 같은 루트의 집계는 반영합니다.
            if (result.Months is not null) RenderTimeline(result.Months);
            if (version != _queryVersion || offset != _offset) return;
            _listedFrom = from; _listedTo = to;
            var rows = result.Rows;
            var selected = (PhotoList.SelectedItem as PhotoItem)?.Record.RelativePath;
            var items = rows.Take(PageSize).Select(row => new PhotoItem(row)).ToList();
            if (PhotoList.ItemsSource is not IReadOnlyList<PhotoItem> previous || !previous.SequenceEqual(items))
            {
                PhotoList.ItemsSource = items;
                if (selected is not null) PhotoList.SelectedItem = items.FirstOrDefault(item => item.Record.RelativePath == selected);
            }
            NextPage.IsEnabled = rows.Count > PageSize;
            PreviousPage.IsEnabled = offset > 0;
            PageLabel.Text = $"{offset / PageSize + 1}쪽 · {items.Count}건";
        }
        catch (Exception exception) { if (!_closed) Status.Text = "목록 조회 실패 · " + exception.Message; }
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e) { if (_root is not null) await ScanFolderAsync(_root); }
    private void CancelScan_Click(object sender, RoutedEventArgs e) => _scan?.Cancel();
    private async void Apply_Click(object sender, RoutedEventArgs e) { _timelineKey = null; UpdateTimelineSelection(); _offset = 0; await RefreshRowsAsync(); }
    private async void Clear_Click(object sender, RoutedEventArgs e) { FromDate.SelectedDate = ToDate.SelectedDate = null; _timelineKey = "all"; UpdateTimelineSelection(); _offset = 0; await RefreshRowsAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { _offset = Math.Max(0, _offset - PageSize); await RefreshRowsAsync(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { _offset = checked(_offset + PageSize); await RefreshRowsAsync(); }

    private void ResetTimeline()
    {
        _updatingTimeline = true;
        Timeline.Items.Clear();
        _timelineKey = "all";
        TimelineSummary.Text = "폴더 전체 집계 · 수집 완료/취소 후 갱신됩니다.";
        _updatingTimeline = false;
    }

    private void RenderTimeline(IReadOnlyList<MonthSummary> months)
    {
        _updatingTimeline = true;
        try
        {
            Timeline.Items.Clear();
            var count = months.Sum(month => month.Count);
            TimelineSummary.Text = $"폴더 전체 {count:N0}개 · 추정 UTC {months.Sum(month => month.EstimatedCount):N0} · 오류/미검증 {months.Sum(month => month.ErrorCount):N0}";
            Timeline.Items.Add(new TreeViewItem { Header = $"전체 날짜 · {count:N0}개", Tag = new TimelineChoice("all", null, null) });
            // ponytail: 월 그룹을 모두 생성합니다. 비정상적으로 긴 연대의 대량 집계는 계층 가상화로 보완합니다.
            foreach (var year in months.GroupBy(month => month.Month[..4]))
            {
                var number = int.Parse(year.Key, CultureInfo.InvariantCulture);
                var yearItem = new TreeViewItem
                {
                    Header = $"{year.Key}년 · {year.Sum(month => month.Count):N0}개",
                    Tag = new TimelineChoice(year.Key, new DateTime(number, 1, 1), new DateTime(number, 12, 31)),
                    ToolTip = $"추정 UTC {year.Sum(month => month.EstimatedCount):N0} · 오류/미검증 {year.Sum(month => month.ErrorCount):N0}",
                    IsExpanded = _timelineKey?.StartsWith(year.Key, StringComparison.Ordinal) == true || year.Key == months[0].Month[..4]
                };
                foreach (var month in year)
                {
                    var start = DateTime.ParseExact(month.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    yearItem.Items.Add(new TreeViewItem
                    {
                        Header = $"{start.Month:00}월 · {month.Count:N0}개 · 추정 {month.EstimatedCount:N0}",
                        Tag = new TimelineChoice(month.Month, start, new DateTime(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month))),
                        ToolTip = $"오류/미검증 {month.ErrorCount:N0}개 포함 · 촬영일 또는 추정 UTC의 달력 날짜"
                    });
                }
                Timeline.Items.Add(yearItem);
            }
        }
        finally { _updatingTimeline = false; }
        UpdateTimelineSelection();
    }

    private void UpdateTimelineSelection()
    {
        _updatingTimeline = true;
        try
        {
            foreach (var parent in Timeline.Items.Cast<TreeViewItem>())
                foreach (var item in new[] { parent }.Concat(parent.Items.Cast<TreeViewItem>()))
                    item.IsSelected = item.Tag is TimelineChoice choice && choice.Key == _timelineKey;
        }
        finally { _updatingTimeline = false; }
    }

    private async void Timeline_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_updatingTimeline || e.NewValue is not TreeViewItem { Tag: TimelineChoice choice } || choice.Key == _timelineKey) return;
        _timelineKey = choice.Key;
        FromDate.SelectedDate = choice.From;
        ToDate.SelectedDate = choice.To;
        _offset = 0;
        await RefreshRowsAsync();
    }

    private async void PhotoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PreviewTask = ShowPreviewAsync(PhotoList.SelectedItem as PhotoItem);
        await PreviewTask;
    }

    private async Task ShowPreviewAsync(PhotoItem? item)
    {
        var version = ++_previewVersion;
        MapPanel.SetPhoto(item?.Record);
        VideoPanel.Reset();
        VideoPanel.Visibility = Visibility.Collapsed;
        Viewer.Visibility = Visibility.Visible;
        ImageTools.IsEnabled = true;
        Preview.Source = null;
        Preview.Width = Preview.Height = 0;
        if (item is null) { Metadata.Text = ""; PreviewHint.Text = "사진을 선택하세요."; return; }
        var row = item.Record;
        var gps = GpsMetadata.Read(row.ExifJson);
        var location = Catalog.IsVideo(row.RelativePath) ? "GPS · 영상 위치는 아직 미추출"
            : row.Error is not null ? "GPS · 미검증/읽기 오류로 위치 미확정"
            : gps.Coordinates is { } point ? FormattableString.Invariant($"원본 EXIF GPS · 위도 {point.Latitude:F6} · 경도 {point.Longitude:F6}")
            : gps.Error == "GPS_INCOMPLETE" ? "GPS · 일부 태그 누락으로 위치 미확정"
            : gps.Error is not null ? "GPS · 형식/값 오류로 위치 미확정"
            : "GPS · 정보 없음(위치 추정하지 않음)";
        Metadata.Text = $"{row.RelativePath}\n{item.DateLabel} · 오프셋 {row.Offset ?? "정보 없음"}\n상태 {item.StateLabel}\n{location}\n행정구역 자동 분류 · 경계 데이터 검증 대기\n원본 태그 · {row.ExifJson[..Math.Min(20000, row.ExifJson.Length)]}";
        if (row.Error is not null) { PreviewHint.Text = $"{item.StateLabel} · {row.Error} · 원본을 수정하지 않았습니다."; return; }
        if (Catalog.IsVideo(row.RelativePath))
        {
            Viewer.Visibility = Visibility.Collapsed;
            VideoPanel.Visibility = Visibility.Visible;
            ImageTools.IsEnabled = false;
            PreviewHint.Text = "동영상 · 촬영일/GPS 미추출(파일 수정 UTC 사용) · 원본 변환 없음";
            try { await VideoPanel.LoadAsync(row.Root, row.RelativePath); }
            catch (Exception exception) { if (version == _previewVersion && !_closed) PreviewHint.Text = "동영상 열기 실패 · " + exception.Message; }
            return;
        }
        PreviewHint.Text = "미리보기 생성 중…";
        await _previewGate.WaitAsync();
        try
        {
            if (version != _previewVersion || _closed) return;
            var bitmap = await Task.Run(() => PreviewLoader.Load(row.Root, row.RelativePath));
            if (version != _previewVersion || _closed) return;
            Preview.Source = bitmap;
            Preview.Width = bitmap.PixelWidth;
            Preview.Height = bitmap.PixelHeight;
            PreviewHint.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} 미리보기 · 긴 변 최대 2400px · 드래그 이동 · Ctrl+휠 확대";
            FitPreview();
        }
        catch (Exception exception) { if (version == _previewVersion && !_closed) PreviewHint.Text = "미리보기 실패 · " + exception.Message; }
        finally { _previewGate.Release(); }
    }

    internal void FitPreview()
    {
        if (Preview.Source is not BitmapSource bitmap) return;
        Zoom.Value = Math.Clamp(100 * Math.Min(Math.Max(1, Viewer.ViewportWidth - 20) / bitmap.PixelWidth,
            Math.Max(1, Viewer.ViewportHeight - 20) / bitmap.PixelHeight), 10, 800);
        Viewer.ScrollToHome();
    }
    private void Fit_Click(object sender, RoutedEventArgs e) => FitPreview();
    private void Zoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewScale is not null) PreviewScale.ScaleX = PreviewScale.ScaleY = e.NewValue / 100;
    }
    private void Viewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        Zoom.Value += e.Delta > 0 ? 25 : -25;
        e.Handled = true;
    }
    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { e.Handled = true; CreateSlideshow(false)?.ShowDialog(); return; }
        _dragStart = e.GetPosition(Viewer); _dragHorizontal = Viewer.HorizontalOffset; _dragVertical = Viewer.VerticalOffset;
        Preview.CaptureMouse(); Preview.Cursor = Cursors.Hand; e.Handled = true;
    }
    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(Viewer);
        Viewer.ScrollToHorizontalOffset(_dragHorizontal - point.X + start.X);
        Viewer.ScrollToVerticalOffset(_dragVertical - point.Y + start.Y);
    }
    private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null; Preview.ReleaseMouseCapture(); Preview.Cursor = Cursors.Arrow;
    }
    private void FullScreen_Click(object sender, RoutedEventArgs e) => CreateSlideshow(false)?.ShowDialog();
    private void Slideshow_Click(object sender, RoutedEventArgs e) => CreateSlideshow(true)?.ShowDialog();

    internal SlideshowWindow? CreateSlideshow(bool autoPlay)
    {
        if (_scan is not null || !_queryTask.IsCompleted || _root is null || !File.Exists(_database))
        { Status.Text = "사진 폴더 수집을 완료한 뒤 실행하세요."; return null; }
        if (!SlideshowWindow.TrySeconds(SlideInterval.Text, out var seconds))
        { Status.Text = "슬라이드쇼 간격은 1~300초의 정수로 입력하세요."; SlideInterval.Focus(); return null; }
        if (!autoPlay && PhotoList.SelectedItem is not PhotoItem { Record.Error: null })
        { Status.Text = "정상 사진을 선택한 뒤 전체 화면을 여세요."; return null; }
        if (!autoPlay && PhotoList.SelectedItem is PhotoItem selected && Catalog.IsVideo(selected.Record.RelativePath))
        { Status.Text = "사진 전체 화면에서는 동영상을 재생하지 않습니다."; return null; }
        var show = new SlideshowWindow(_database, _root, _listedFrom, _listedTo,
            PhotoList.SelectedIndex < 0 ? 0 : _offset + PhotoList.SelectedIndex, seconds, autoPlay) { Owner = this };
        show.Closed += (_, _) => SlideInterval.Text = show.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
        return show;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_scan is not null) { e.Cancel = true; _closeAfterScan = true; _scan.Cancel(); Status.Text = "저장된 항목을 보존하고 종료하는 중…"; }
        else { _closed = true; _previewVersion++; _timer.Stop(); MapPanel.Dispose(); VideoPanel.Dispose(); }
    }
}
