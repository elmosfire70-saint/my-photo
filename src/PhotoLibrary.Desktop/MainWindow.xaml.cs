// 읽기 전용 수집·필터·페이지와 비동기 미리보기의 화면 상태를 연결합니다.
using System.ComponentModel;
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
    public string StateLabel => Record.Error switch { null => "정상", "UNVERIFIED_FORMAT" => "미검증 형식", _ => "읽기 오류" };
}

public partial class MainWindow : Window
{
    private const int PageSize = 200;
    private string _database;
    private string? _root;
    private int _offset, _previewVersion, _queryVersion;
    private CancellationTokenSource? _scan;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _previewGate = new(1);
    private bool _closed, _closeAfterScan;
    private Point? _dragStart;
    private double _dragHorizontal, _dragVertical;
    internal Task PreviewTask { get; private set; } = Task.CompletedTask;
    internal Task QueryTask => _queryTask;
    private Task _queryTask = Task.CompletedTask;

    public MainWindow(string database)
    {
        InitializeComponent();
        _database = Path.GetFullPath(database);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _timer.Tick += async (_, _) => { if (_queryTask.IsCompleted) await RefreshRowsAsync(); };
        Closing += OnClosing;
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
        }
    }

    internal async Task ScanFolderAsync(string root)
    {
        if (_scan is not null) return;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _offset = 0;
        PhotoList.ItemsSource = null;
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
            await RefreshRowsAsync();
            if (_closeAfterScan) Close();
        }
    }

    internal Task RefreshRowsAsync()
    {
        _queryTask = RefreshAfterAsync(_queryTask, ++_queryVersion);
        return _queryTask;
    }

    private async Task RefreshAfterAsync(Task prior, int version)
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
            var rows = await Task.Run(() => Catalog.List(database, from, to, PageSize + 1, root, offset));
            if (_closed || version != _queryVersion || root != _root || database != _database || offset != _offset) return;
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
    private async void Apply_Click(object sender, RoutedEventArgs e) { _offset = 0; await RefreshRowsAsync(); }
    private async void Clear_Click(object sender, RoutedEventArgs e) { FromDate.SelectedDate = ToDate.SelectedDate = null; _offset = 0; await RefreshRowsAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { _offset = Math.Max(0, _offset - PageSize); await RefreshRowsAsync(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { _offset = checked(_offset + PageSize); await RefreshRowsAsync(); }

    private async void PhotoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PreviewTask = ShowPreviewAsync(PhotoList.SelectedItem as PhotoItem);
        await PreviewTask;
    }

    private async Task ShowPreviewAsync(PhotoItem? item)
    {
        var version = ++_previewVersion;
        Preview.Source = null;
        Preview.Width = Preview.Height = 0;
        if (item is null) { Metadata.Text = ""; PreviewHint.Text = "사진을 선택하세요."; return; }
        var row = item.Record;
        Metadata.Text = $"{row.RelativePath}\n{item.DateLabel} · 오프셋 {row.Offset ?? "정보 없음"}\n상태 {item.StateLabel}\n원본 태그 · {row.ExifJson[..Math.Min(20000, row.ExifJson.Length)]}";
        if (row.Error is not null) { PreviewHint.Text = $"{item.StateLabel} · {row.Error} · 원본을 수정하지 않았습니다."; return; }
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
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_scan is not null) { e.Cancel = true; _closeAfterScan = true; _scan.Cancel(); Status.Text = "저장된 항목을 보존하고 종료하는 중…"; }
        else { _closed = true; _previewVersion++; _timer.Stop(); }
    }
}
