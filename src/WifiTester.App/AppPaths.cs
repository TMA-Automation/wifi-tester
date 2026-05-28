using System.IO;

namespace WifiTester.App;

internal static class AppPaths
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WifiTester");
    public static string Config => Path.Combine(Dir, "config.json");
    public static string Database => Path.Combine(Dir, "wifitester.db");
    public static void Ensure() => Directory.CreateDirectory(Dir);
}
