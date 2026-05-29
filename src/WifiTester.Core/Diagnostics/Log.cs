namespace WifiTester.Core.Diagnostics;

/// Prosty logger do pliku (apka jest okienkowa — Console.Error jest niewidoczny).
/// Wątkowo bezpieczny dopis; ciche pominięcie błędów IO (log nie może wywalić aplikacji).
public static class Log
{
    private static string? _path;
    private static readonly object _gate = new();

    public static void Init(string path) => _path = path;

    public static void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}";
        try
        {
            if (_path is not null)
                lock (_gate) File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch { /* log nie może rzucać */ }
        Console.Error.WriteLine(line);
    }
}
