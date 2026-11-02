// 공개 HEIC와 합성 동영상으로 아이폰 미디어 디코딩·재생을 검증합니다.
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal static class IphoneSmokeTest
{
    internal static async Task RunAsync(MainWindow window, string output, string fixtures)
    {
        output = Path.GetFullPath(output);
        fixtures = Path.GetFullPath(fixtures);
        var allowed = Path.GetFullPath("test-artifacts") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !fixtures.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output)) throw new IOException("Use test-artifacts fixtures and a new test output directory.");
        for (var parent = Directory.GetParent(output); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("No linked test paths.");
        var root = Path.Combine(output, "아이폰 시험 복사본");
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "example.heic", "h264.mp4", "hevc10.mov", "hevc10.mp4" })
            File.Copy(Catalog.ResolveMediaPath(fixtures, name), Path.Combine(root, name));
        File.Copy(Path.Combine(root, "example.heic"), Path.Combine(root, "alias.HEIF"));
        File.WriteAllText(Path.Combine(root, "broken.mp4"), "not a video");
        File.WriteAllText(Path.Combine(root, "broken.heic"), "not a photo");
        foreach (var path in Directory.GetFiles(root)) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        var hashes = Directory.GetFiles(root).ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        await window.ScanFolderAsync(root);
        Check(window.PhotoList.Items.Count == 7, "HEIC/HEIF and video indexed");
        var items = window.PhotoList.Items.Cast<PhotoItem>().ToList();
        foreach (var name in new[] { "example.heic", "alias.HEIF" })
        {
            var item = items.Single(item => item.Record.RelativePath == name);
            Check(item.Record.Error is null && item.Record.Width == 1280 && item.Record.Height == 854, "HEIC metadata " + name);
            window.PhotoList.SelectedItem = item;
            await window.PreviewTask;
            Check(window.Preview.Source is BitmapSource { PixelWidth: 1280, PixelHeight: 854 }, "HEIC preview " + name);
        }
        foreach (var name in new[] { "h264.mp4", "hevc10.mov", "hevc10.mp4" })
        {
            window.PhotoList.SelectedItem = items.Single(item => item.Record.RelativePath == name);
            await window.PreviewTask;
            var pane = window.VideoPanel;
            var player = pane.Player ?? throw new Exception("FAIL: video player not created");
            player.Mute = true;
            Check(pane.EngineVersion.StartsWith("3.0.24") && pane.PlayButton.IsEnabled && !player.IsPlaying,
                "latest stable engine ready without autoplay " + name);
            using var media = player.Media ?? throw new Exception("FAIL: video media missing");
            Click(pane.PlayButton);
            await UntilAsync(() => player.IsPlaying && player.Time > 250 && media.Statistics is { DecodedVideo: > 0 }, "decode " + name);
            Click(pane.PauseButton);
            await UntilAsync(() => player.State == VLCState.Paused, "pause " + name);
            Check(media.Statistics is { DecodedAudio: > 0 }, "AAC audio decoded while muted " + name);
            var snapshot = Path.Combine(output, name + "-frame.png");
            Check(player.TakeSnapshot(0, snapshot, 0, 0), "decoded video snapshot requested " + name);
            await UntilAsync(() => File.Exists(snapshot) && new FileInfo(snapshot).Length > 512, "snapshot saved " + name);
            using (var frame = new ImageMagick.MagickImage(snapshot))
                Check(frame.Width == 320 && frame.Height == 180, "snapshot pixel dimensions " + name);
            await UntilAsync(() => player.IsSeekable, "seekable " + name);
            pane.Seek.Value = 0.6;
            await UntilAsync(() => player.Position > 0.45, "seek " + name);
            pane.Volume.Value = 42;
            await UntilAsync(() => player.Volume == 42, "volume " + name);
            var decodedBeforeResume = media.Statistics is { } previousStats ? previousStats.DecodedVideo : 0;
            Click(pane.PlayButton);
            await UntilAsync(() => player.IsPlaying, "resume " + name);
            await UntilAsync(() => player.Time >= 6000 && media.Statistics is { } afterStats && afterStats.DecodedVideo > decodedBeforeResume,
                "resumed video advances and decodes " + name);
            Click(pane.StopButton);
            await UntilAsync(() => !player.IsPlaying, "stop " + name);
        }
        window.PhotoList.SelectedItem = items.Single(item => item.Record.RelativePath == "broken.mp4");
        await window.PreviewTask;
        Check(!window.VideoPanel.PlayButton.IsEnabled && window.PreviewHint.Text.Contains("실패"), "invalid video fails visibly");
        window.PhotoList.SelectedItem = items.Single(item => item.Record.RelativePath == "h264.mp4");
        await window.PreviewTask;
        window.VideoPanel.Player!.Mute = true;
        Click(window.VideoPanel.PlayButton);
        await UntilAsync(() => window.VideoPanel.Player.IsPlaying, "switch test playing");
        window.PhotoList.SelectedItem = items.Single(item => item.Record.RelativePath == "example.heic");
        await window.PreviewTask;
        Check(!window.VideoPanel.Player.IsPlaying && window.VideoPanel.Visibility == Visibility.Collapsed, "photo selection stops video");
        var closing = new MainWindow(Path.Combine(output, "catalog.sqlite")) { ShowInTaskbar = false, ShowActivated = false };
        closing.Show();
        await closing.ScanFolderAsync(root);
        closing.PhotoList.SelectedItem = closing.PhotoList.Items.Cast<PhotoItem>().Single(item => item.Record.RelativePath == "hevc10.mov");
        await closing.PreviewTask;
        closing.VideoPanel.Player!.Mute = true;
        Click(closing.VideoPanel.PlayButton);
        await UntilAsync(() => closing.VideoPanel.Player.IsPlaying, "close test playing");
        closing.Close();
        Check(!closing.IsVisible, "close while playing completes");
        Check(hashes.All(pair => pair.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pair.Key)))), "all source hashes unchanged");
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: HEIC/HEIF, H264/AAC MP4, HEVC Main10 MOV/MP4; original hashes unchanged. HDR color accuracy unverified.");
        Console.WriteLine("PASS: iPhone media integration test completed");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task UntilAsync(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(12)) throw new Exception("FAIL: timeout " + message);
            await Task.Delay(40);
        }
        Console.WriteLine("PASS: " + message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
