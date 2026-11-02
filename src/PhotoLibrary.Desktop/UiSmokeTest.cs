// 합성 사진만으로 WPF 목록·필터·미리보기와 확대 스크롤을 검사합니다.
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal static class UiSmokeTest
{
    internal static async Task RunAsync(MainWindow window, string output)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("test-artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output)) throw new IOException("UI test requires a new directory under test-artifacts.");
        for (var parent = Directory.GetParent(output); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("UI test does not follow links or junctions.");
        var root = Path.Combine(output, "합성 사진");
        Directory.CreateDirectory(Path.Combine(root, "more"));
        var jpeg = Path.Combine(root, "2020-겨울 여행.jpg");
        using (var image = new MagickImage("gradient:#142e5e-#75d9eb", 2000, 1400))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2020:01:02 03:04:05");
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLongitudeRef, "E");
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(37u), new Rational(30u), new Rational(0u) });
            exif.SetValue(ExifTag.GPSLongitude, new[] { new Rational(126u), new Rational(15u), new Rational(0u) });
            image.SetProfile(exif);
            image.Write(jpeg);
        }
        var png = Path.Combine(root, "2021-봄 산책.png");
        using (var image = new MagickImage(MagickColors.SeaGreen, 900, 1200))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2021:04:10 12:00:00");
            image.SetProfile(exif);
            image.Write(png);
        }
        for (var i = 0; i < 201; i++) File.Copy(png, Path.Combine(root, "more", $"산책-{i:000}.png"));
        foreach (var sample in new[] { (0, "2020:02:29 23:59:59"), (1, "2020:12:31 23:59:59") })
        {
            var path = Path.Combine(root, "more", $"산책-{sample.Item1:000}.png");
            using var image = new MagickImage(path);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, sample.Item2);
            image.SetProfile(exif);
            image.Write(path);
        }
        File.WriteAllText(Path.Combine(root, "손상.jpg"), "not an image");
        File.WriteAllText(Path.Combine(root, "미검증.cr2"), "synthetic placeholder");
        File.SetAttributes(jpeg, File.GetAttributes(jpeg) | FileAttributes.ReadOnly);
        var hashes = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var scan = window.ScanFolderAsync(root);
        Check(!window.ChooseFolder.IsEnabled && window.CancelScan.IsEnabled, "background scan controls");
        await scan;
        Check(window.Status.Text.StartsWith("수집 완료"), "scan completed through UI");
        Check(window.PhotoList.Items.Count == 200 && window.NextPage.IsEnabled, "first page is bounded");
        Click(window.NextPage);
        await window.QueryTask;
        Check(window.PhotoList.Items.Count == 5 && window.PreviousPage.IsEnabled && !window.NextPage.IsEnabled, "next page");
        Check(window.TimelineSummary.Text.Contains("205") && window.Timeline.Items.Count >= 3, "timeline counts all pages");
        var year2020 = window.Timeline.Items.Cast<TreeViewItem>().Single(item => item.Header.ToString()!.StartsWith("2020년"));
        year2020.IsExpanded = true;
        await IdleAsync(window);
        year2020.Items.Cast<TreeViewItem>().Single(item => item.Header.ToString()!.StartsWith("02월")).IsSelected = true;
        await window.QueryTask;
        Check(window.FromDate.SelectedDate == new DateTime(2020, 2, 1) && window.ToDate.SelectedDate == new DateTime(2020, 2, 29)
            && window.PhotoList.Items.Count == 1 && !window.PreviousPage.IsEnabled, "timeline leap month resets page and dates");
        year2020.IsSelected = true;
        await window.QueryTask;
        Check(window.FromDate.SelectedDate == new DateTime(2020, 1, 1) && window.ToDate.SelectedDate == new DateTime(2020, 12, 31)
            && window.PhotoList.Items.Count == 3, "timeline year includes all three months");
        year2020.Items.Cast<TreeViewItem>().Single(item => item.Header.ToString()!.StartsWith("12월")).IsSelected = true;
        await window.QueryTask;
        Check(window.ToDate.SelectedDate == new DateTime(2020, 12, 31) && window.PhotoList.Items.Count == 1, "timeline December bounds");
        Click(window.ClearFilter);
        await window.QueryTask;
        Check(window.PhotoList.Items.Count == 200 && window.FromDate.SelectedDate is null && window.ToDate.SelectedDate is null,
            "timeline clear restores all dates");
        window.FromDate.SelectedDate = new DateTime(2005, 1, 1);
        window.ToDate.SelectedDate = new DateTime(2005, 12, 31);
        Click(window.ApplyFilter);
        await window.QueryTask;
        Check(window.PhotoList.Items.Count == 0 && window.TimelineSummary.Text.Contains("205"), "empty period retains whole-root timeline");
        await Select2020Async(window);
        Check(window.Timeline.SelectedItem is null, "manual date filter clears tree selection");
        Check(window.PhotoList.Items.Count == 1, "UI inclusive date filter");
        window.PhotoList.SelectedIndex = 0;
        await window.PreviewTask;
        Check(window.Preview.Source is BitmapSource { PixelWidth: 2000, PixelHeight: 1400 }, "read-only preview decoded");
        Check(window.Metadata.Text.Contains("37.500000") && window.Metadata.Text.Contains("126.250000")
            && window.Metadata.Text.Contains("경계 데이터 검증 대기"), "raw GPS shown without invented region");
        using (var unlocked = new FileStream(jpeg, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(unlocked.Length > 0, "preview released original file handle");
        try
        {
            PreviewLoader.Load(root, "../outside.jpg");
            throw new Exception("FAIL: preview root escape accepted");
        }
        catch (IOException) { Check(true, "preview root escape rejected"); }
        window.Zoom.Value = 800;
        await IdleAsync(window);
        Check(window.Viewer.ScrollableWidth > 0 && window.Viewer.ScrollableHeight > 0, "800 percent scroll extents");
        window.Viewer.ScrollToHorizontalOffset(1000);
        window.Viewer.ScrollToVerticalOffset(600);
        await IdleAsync(window);
        Check(window.Viewer.HorizontalOffset > 0 && window.Viewer.VerticalOffset > 0, "both scroll directions moved");
        Capture(window, Path.Combine(output, "zoom.png"));
        Click(window.ClearFilter);
        await window.QueryTask;
        var healthy = window.PhotoList.Items.Cast<PhotoItem>().First(item => item.Record.Error is null);
        var broken = window.PhotoList.Items.Cast<PhotoItem>().First(item => item.Record.Error == "INVALID_IMAGE");
        window.PhotoList.SelectedItem = healthy;
        var oldPreview = window.PreviewTask;
        window.PhotoList.SelectedItem = broken;
        await window.PreviewTask;
        await oldPreview;
        Check(window.Preview.Source is null && window.PreviewHint.Text.Contains("INVALID_IMAGE"), "stale preview discarded and error shown");
        Check(window.Metadata.Text.Contains("읽기 오류로 위치 미확정"), "unreadable photo GPS remains unknown");
        window.PhotoList.SelectedItem = healthy;
        await window.PreviewTask;
        Check(window.Metadata.Text.Contains("정보 없음(위치 추정하지 않음)"), "absent GPS is not inferred");
        var canceled = window.ScanFolderAsync(root);
        Click(window.CancelScan);
        await canceled;
        Check(window.Status.Text.Contains("취소") && Catalog.List(Path.Combine(output, "catalog.sqlite"), limit: 1000).Count == 205,
            "UI cancellation preserves database");
        var closingWindow = new MainWindow(Path.Combine(output, "catalog.sqlite")) { ShowActivated = false, ShowInTaskbar = false };
        closingWindow.Show();
        var closingScan = closingWindow.ScanFolderAsync(root);
        closingWindow.Close();
        Check(closingWindow.IsVisible, "close waits for scan cancellation");
        await closingScan;
        Check(!closingWindow.IsVisible, "close completes after checkpoint handling");
        var emptyRoot = Path.Combine(output, "빈 폴더");
        Directory.CreateDirectory(emptyRoot);
        await window.ScanFolderAsync(emptyRoot);
        Check(window.PhotoList.Items.Count == 0 && window.Timeline.Items.Count == 1
            && window.Timeline.Items[0] is TreeViewItem empty && empty.Header.ToString()!.Contains("0개"), "empty folder clears previous timeline");
        Check(Catalog.List(Path.Combine(output, "catalog.sqlite"), root: root, limit: 1000).Count == 205, "empty folder does not delete previous root");
        await window.ScanFolderAsync(root);
        await Select2020Async(window);
        window.PhotoList.SelectedIndex = 0;
        await window.PreviewTask;
        window.FitPreview();
        await IdleAsync(window);
        Capture(window, Path.Combine(output, "window.png"));
        Check(hashes.All(pair => pair.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pair.Key)))), "all synthetic originals unchanged");
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: WPF smoke test, 205 synthetic inputs; originals unchanged.");
        Console.WriteLine("PASS: WPF smoke test completed");
    }

    private static async Task Select2020Async(MainWindow window)
    {
        window.FromDate.SelectedDate = window.ToDate.SelectedDate = new DateTime(2020, 1, 2);
        Click(window.ApplyFilter);
        await window.QueryTask;
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task IdleAsync(MainWindow window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }
    private static void Capture(MainWindow window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)(content.ActualWidth + content.Margin.Left + content.Margin.Right),
            (int)(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
