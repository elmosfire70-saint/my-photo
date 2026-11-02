// 합성 사진으로 전체 화면·설정 간격·페이지 경계·오류 건너뛰기·종료 보존성을 검사합니다.
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal static class SlideshowSmokeTest
{
    internal static async Task RunAsync(MainWindow window, string output)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("test-artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output)) throw new IOException("Use a new test-artifacts directory.");
        for (var parent = Directory.GetParent(output); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("No linked test paths.");
        var root = Path.Combine(output, "합성 사진");
        Directory.CreateDirectory(root);
        var original = Path.Combine(root, "000.png");
        using (var image = new MagickImage("gradient:#123864-#63dba9", 900, 1200))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2024:01:01 00:00:00");
            image.SetProfile(exif);
            image.Write(original);
        }
        for (var i = 1; i <= 202; i++) File.Copy(original, Path.Combine(root, $"{i:000}.png"));
        foreach (var name in new[] { "198a-broken.jpg", "198b-video.mp4", "198c-unverified.cr2" })
        {
            var path = Path.Combine(root, name);
            File.WriteAllText(path, "synthetic placeholder");
            File.SetLastWriteTimeUtc(path, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        using (var image = new MagickImage(original))
        {
            var exif = new ExifProfile(); exif.SetValue(ExifTag.DateTimeOriginal, "2023:01:01 00:00:00");
            image.SetProfile(exif); image.Write(Path.Combine(root, "outside-date.png"));
        }
        File.SetAttributes(original, File.GetAttributes(original) | FileAttributes.ReadOnly);
        var hashes = Directory.GetFiles(root).ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        await window.ScanFolderAsync(root);
        var foreign = Path.Combine(output, "다른 폴더");
        Directory.CreateDirectory(foreign);
        File.Copy(original, Path.Combine(foreign, "foreign.png"));
        await Task.Run(() => Catalog.Scan(foreign, Path.Combine(output, "catalog.sqlite")));
        window.FromDate.SelectedDate = window.ToDate.SelectedDate = new DateTime(2024, 1, 1);
        Click(window.ApplyFilter); await window.QueryTask;
        window.PhotoList.SelectedItem = window.PhotoList.Items.Cast<PhotoItem>().Single(item => item.Record.RelativePath == "198.png");
        await window.PreviewTask;
        window.Zoom.Value = 321;
        var selected = window.PhotoList.SelectedItem;
        window.SlideInterval.Text = "0";
        Check(window.CreateSlideshow(false) is null, "invalid main interval rejected");
        window.SlideInterval.Text = "5";
        window.FromDate.SelectedDate = window.ToDate.SelectedDate = new DateTime(2000, 1, 1);
        var show = window.CreateSlideshow(false) ?? throw new Exception("fullscreen not created");
        show.Show();
        try
        {
            await ReadyAsync(show);
            Check(show.WindowStyle == WindowStyle.None && show.WindowState == WindowState.Maximized
                && show.Photo.Stretch == Stretch.Uniform && show.Current?.RelativePath == "198.png" && !show.IsPlaying,
                "selected photo fullscreen uses applied list filter without cropping or autoplay");
            Click(show.Next); await show.DisplayTask;
            Check(show.Current?.RelativePath == "199.png" && show.Position >= 200 && show.Hint.Text.Contains("3개"),
                "cross-page navigation skips broken video and raw entries");
            Key(show, System.Windows.Input.Key.Left); await show.DisplayTask;
            Check(show.Current?.RelativePath == "198.png", "previous crosses page and skips in reverse");
            var missing = Path.Combine(root, "199.png");
            var parked = Path.Combine(output, "temporarily-unavailable.png");
            File.Move(missing, parked);
            try
            {
                Click(show.Next); await show.DisplayTask;
                Check(show.Current?.RelativePath == "200.png" && show.Hint.Text.Contains("4개"), "missing file skipped without stopping");
            }
            finally { File.Move(parked, missing); }
            foreach (var invalid in new[] { "", "0", "-1", "301", "1.5", "abc" })
            {
                show.IntervalInput.Text = invalid; Click(show.ApplyInterval);
                Check(show.IntervalSeconds == 5 && show.Hint.Text.Contains("1~300"), "invalid interval preserves setting " + invalid);
            }
            show.IntervalInput.Text = "300"; Click(show.ApplyInterval);
            Check(show.IntervalSeconds == 300, "maximum interval accepted");
            show.IntervalInput.Text = "1"; Click(show.ApplyInterval);
            var position = show.Position;
            Key(show, System.Windows.Input.Key.Space);
            await WaitAsync(() => show.Position != position);
            Click(show.PlayPause); await show.DisplayTask;
            Check(!show.IsPlaying && show.Current?.RelativePath == "201.png", "real one-second timer advances exactly one photo");
            position = show.Position;
            await Task.Delay(1250);
            Check(show.Position == position && !show.IsPlaying, "pause prevents further timer changes");
            Click(show.Previous); await show.DisplayTask;
            show.IntervalInput.Text = "2"; Click(show.ApplyInterval);
            position = show.Position;
            Click(show.PlayPause);
            await Task.Delay(1100);
            Check(show.Position == position, "changed two-second interval does not fire at one second");
            await WaitAsync(() => show.Position != position);
            Click(show.PlayPause); await show.DisplayTask;
            Click(show.Next); await show.DisplayTask;
            Check(show.Current?.RelativePath == "202.png", "last photo respects root and date filter");
            Click(show.Next); await show.DisplayTask;
            Check(!show.IsPlaying && show.Hint.Text.Contains("마지막 사진"), "last photo stops without wrapping");
            Click(show.PlayPause); await show.DisplayTask; Click(show.PlayPause);
            Check(show.Current?.RelativePath == "000.png" && !show.IsPlaying, "replay from end starts at first filtered photo");
            show.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)show.ActualWidth, (int)show.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(show);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "fullscreen.png"))) encoder.Save(stream);
            Key(show, System.Windows.Input.Key.Escape);
            Check(!show.IsVisible && !show.IsPlaying && show.Photo.Source is null, "Esc closes and releases fullscreen image");
        }
        finally { if (show.IsVisible) show.Close(); }
        Check(ReferenceEquals(selected, window.PhotoList.SelectedItem) && window.Zoom.Value == 321
            && window.FromDate.SelectedDate == new DateTime(2000, 1, 1) && window.SlideInterval.Text == "2",
            "main selection date zoom and interval preserved");
        var closing = window.CreateSlideshow(true) ?? throw new Exception("autoplay not created");
        closing.Show(); await ReadyAsync(closing);
        Click(closing.Next); var late = closing.DisplayTask; closing.Close(); await late;
        Check(!closing.IsPlaying && closing.Photo.Source is null, "close during decode rejects late result and timer");
        window.FromDate.SelectedDate = window.ToDate.SelectedDate = new DateTime(2000, 1, 1);
        Click(window.ApplyFilter); await window.QueryTask;
        var empty = window.CreateSlideshow(true) ?? throw new Exception("empty slideshow not created");
        empty.Show(); await ReadyAsync(empty);
        Check(empty.Current is null && !empty.IsPlaying && empty.Hint.Text.Contains("표시"), "empty filtered slideshow stops safely");
        empty.Close();
        Check(hashes.All(pair => pair.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pair.Key)))), "all synthetic originals unchanged");
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: fullscreen and timed slideshow; synthetic originals unchanged.");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task ReadyAsync(SlideshowWindow show)
    {
        await show.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await show.DisplayTask;
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition()) { if (elapsed.Elapsed.TotalSeconds > 10) throw new Exception("timer test timeout"); await Task.Delay(25); }
    }
    private static void Key(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(window), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
