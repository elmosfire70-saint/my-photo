// 일반 WPF 사진함 또는 합성 자료 전용 UI 검사를 실행합니다.
using System.IO;
using System.Windows;

namespace PhotoLibrary.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0 && args is not ["--smoke-test", _])
        {
            Console.Error.WriteLine("Usage: PhotoLibrary.Desktop [--smoke-test NEW_TEST_OUTPUT]");
            return 1;
        }
        var testing = args.Length == 2;
        var database = testing ? Path.Combine(Path.GetFullPath(args[1]), "catalog.sqlite")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyPhoto", "catalog.sqlite");
        var app = new Application();
        var window = new MainWindow(database);
        var result = 0;
        if (testing)
        {
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Loaded += async (_, _) =>
            {
                try { await UiSmokeTest.RunAsync(window, args[1]); }
                catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
                finally { window.Close(); }
            };
        }
        app.Run(window);
        return result;
    }
}
