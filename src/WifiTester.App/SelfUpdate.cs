using System.Diagnostics;
using System.IO;
using System.Net.Http;
using WifiTester.Core.Diagnostics;

namespace WifiTester.App;

/// Pobiera nowy single-file .exe i podmienia działający plik.
/// Działający .exe jest zablokowany, więc podmiany dokonuje proces pomocniczy (.cmd),
/// który czeka na zamknięcie aplikacji, nadpisuje plik i uruchamia nową wersję.
internal static class SelfUpdate
{
    public static async Task DownloadAndApplyAsync(string assetUrl)
    {
        var current = Environment.ProcessPath
            ?? throw new InvalidOperationException("Nieznana ścieżka procesu.");
        var newExe = Path.Combine(Path.GetTempPath(), "WifiTester.App.new.exe");

        Log.Write($"[UPDATE] pobieranie {assetUrl}");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        {
            http.DefaultRequestHeaders.Add("User-Agent", "TMA-App");
            var bytes = await http.GetByteArrayAsync(assetUrl);
            await File.WriteAllBytesAsync(newExe, bytes);
        }
        Log.Write("[UPDATE] pobrano, podmiana i restart");

        var pid = Environment.ProcessId;
        var cmd = Path.Combine(Path.GetTempPath(), "wifitester_update.cmd");
        File.WriteAllText(cmd,
$"""
@echo off
:loop
tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul
if not errorlevel 1 ( timeout /t 1 >nul & goto loop )
move /Y "{newExe}" "{current}" >nul
start "" "{current}"
del "%~f0"
""");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
