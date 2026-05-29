using WifiTester.Core.Updates;
using Xunit;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("1.1", "1.0", 1)]      // nowsza minor
    [InlineData("1.0", "1.1", -1)]     // starsza minor
    [InlineData("1.0", "1.0", 0)]      // równe
    [InlineData("1.10", "1.9", 1)]     // porównanie liczbowe, nie tekstowe
    [InlineData("2.0", "1.99", 1)]     // major przeważa
    [InlineData("1.0.1", "1.0", 1)]    // dłuższa wersja > krótsza
    [InlineData("1.0", "1.0.0", 0)]    // brakujące człony = 0
    public void Compares_versions_as_numeric_tuples(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(UpdateService.CompareVersions(a, b)));
    }

    [Fact]
    public void Tolerates_bom_and_whitespace_in_version()
    {
        // version.txt z BOM/spacjami nie może psuć wykrywania aktualizacji.
        Assert.True(UpdateService.CompareVersions("﻿1.2", "1.1") > 0);
        Assert.Equal(0, UpdateService.CompareVersions(" 1.1 ", "1.1"));
    }

    [Fact]
    public async Task Check_returns_null_on_unreachable_repo()
    {
        // Repo nie istnieje → cicho null (brak aktualizacji), nigdy wyjątek.
        var result = await UpdateService.CheckAsync(
            "TMA-Automation/__nieistniejace_repo_xyz__", "1.0", "App.exe");
        Assert.Null(result);
    }
}
