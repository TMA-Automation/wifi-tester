using System.Text;
using System.Text.Json;

namespace WifiTester.Core.Updates;

public sealed record UpdateCheck(
    string Latest, string Current, bool Available, string? ReleaseUrl, string? AssetUrl);

/// Auto-aktualizacja wg standardu TMA: pobiera version.txt z repo przez GitHub API,
/// porównuje wersje jako krotki liczb i (gdy nowsza) zwraca URL assetu z release.
/// Brak sieci/repo → null („brak aktualizacji"), nigdy wyjątek dla użytkownika.
public static class UpdateService
{
    /// Porównanie wersji „a vs b" jako krotek liczb: dodatnie gdy a > b, ujemne gdy a < b.
    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? Digits(pa[i]) : 0;
            int y = i < pb.Length ? Digits(pb[i]) : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    // Wyłuskuje liczbę z segmentu, ignorując nie-cyfry (np. BOM/spacje), by porównanie było odporne.
    private static int Digits(string segment)
    {
        var s = new string(segment.Where(char.IsDigit).ToArray());
        return int.TryParse(s, out var n) ? n : 0;
    }

    public static async Task<UpdateCheck?> CheckAsync(
        string repo, string current, string assetName, string? token = null, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.Add("User-Agent", "TMA-App");
            http.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Add("Authorization", "token " + token);

            var verJson = await http.GetStringAsync(
                $"https://api.github.com/repos/{repo}/contents/version.txt", ct);
            using var verDoc = JsonDocument.Parse(verJson);
            var b64 = verDoc.RootElement.GetProperty("content").GetString()!.Replace("\n", "");
            // TrimStart('﻿') usuwa ewentualny BOM — char.IsWhiteSpace go nie łapie, więc Trim() nie wystarcza.
            var latest = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).TrimStart('﻿').Trim();

            var available = CompareVersions(latest, current) > 0;
            if (!available)
                return new UpdateCheck(latest, current, false, null, null);

            var releaseUrl = $"https://github.com/{repo}/releases/tag/v{latest}";
            var assetUrl = await GetAssetUrlAsync(http, repo, latest, assetName, ct);
            return new UpdateCheck(latest, current, true, releaseUrl, assetUrl);
        }
        catch { return null; }
    }

    private static async Task<string?> GetAssetUrlAsync(
        HttpClient http, string repo, string version, string assetName, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync(
                $"https://api.github.com/repos/{repo}/releases/tags/v{version}", ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (string.Equals(asset.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString();
            }
        }
        catch { /* asset opcjonalny — pasek i tak otworzy stronę release */ }
        return null;
    }
}
