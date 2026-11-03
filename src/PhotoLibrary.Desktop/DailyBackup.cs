// 사용자가 지정한 백업 폴더를 보관하고 DB별 날짜별 검증된 스냅샷을 만듭니다.
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoLibrary.Index;

namespace PhotoLibrary.Desktop;

internal sealed class DailyBackup(string settingsFile)
{
    private sealed record Settings(string? Folder);
    internal string? Folder()
    {
        RejectLinks(settingsFile);
        if (!File.Exists(settingsFile)) return null;
        if (new FileInfo(settingsFile).Length > 16_384) throw new IOException("백업 설정 파일이 너무 큽니다.");
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsFile))
            ?? throw new IOException("백업 설정을 읽을 수 없습니다.");
        if (settings.Folder is not null && !Path.IsPathFullyQualified(settings.Folder))
            throw new IOException("백업 폴더 설정은 절대 경로여야 합니다.");
        return settings.Folder;
    }

    internal void SetFolder(string? folder)
    {
        if (folder is not null)
        {
            folder = Path.GetFullPath(folder); RejectLinks(folder);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("백업 폴더가 없습니다.");
        }
        RejectLinks(settingsFile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsFile))!);
        var temporary = settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(folder)));
            File.Move(temporary, settingsFile, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal string Run(string database, DateOnly day)
    {
        var folder = Folder();
        if (folder is null) return "자동 백업 꺼짐 · 폴더를 지정하세요.";
        RejectLinks(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("백업 폴더가 연결되어 있지 않습니다.");
        if (!File.Exists(database)) return "자동 백업 대기 · 수집한 DB가 없습니다.";
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(database).ToUpperInvariant())));
        var target = Path.Combine(folder, $"daily-{identity}-{day:yyyy-MM-dd}.sqlite");
        if (File.Exists(target)) { Catalog.Verify(target); return "오늘 자동 백업 확인 완료 · " + target; }
        Catalog.Backup(database, target);
        return "자동 백업 검증 완료 · " + target;
    }

    private static void RejectLinks(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName))
                && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("백업 설정과 폴더에는 링크 경로를 사용할 수 없습니다.");
    }
}
