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
        File.WriteAllText(Path.Combine(root, "손상.jpg"), "not an image");
        File.WriteAllText(Path.Combine(root, "미검증.heic"), "synthetic placeholder");
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
        await Select2020Async(window);
        Check(window.PhotoList.Items.Count == 1, "UI inclusive date filter");
        window.PhotoList.SelectedIndex = 0;
        await window.PreviewTask;
        Check(window.Preview.Source is BitmapSource { PixelWidth: 2000, PixelHeight: 1400 }, "read-only preview decoded");
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
