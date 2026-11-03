// 명시한 사진 폴더의 읽기 전용 수집·날짜 조회·합성 검사를 실행합니다.
using System.Globalization;
using System.Text.Json;
using PhotoLibrary.Index;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args is ["--self-test", var output]) SelfTest.Run(output);
    else if (args is ["backup", var sourceDatabase, var destination]) Catalog.Backup(sourceDatabase, destination);
    else if (args is ["scan", var root, var database])
        Console.WriteLine(JsonSerializer.Serialize(Catalog.Scan(root, database, cancellation.Token)));
    else if (args is ["list", var path])
        Console.WriteLine(JsonSerializer.Serialize(Catalog.List(path)));
    else if (args is ["list", var dateDatabase, var from, var to])
        Console.WriteLine(JsonSerializer.Serialize(Catalog.List(dateDatabase,
            DateOnly.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture))));
    else throw new ArgumentException("Usage: scan SOURCE DATABASE | list DATABASE [FROM_DATE TO_DATE] | backup DATABASE NEW_FILE | --self-test NEW_TEST_OUTPUT");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("취소했습니다. 같은 명령을 다시 실행하면 저장된 항목부터 재사용합니다.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
