using Dapper;
using Microsoft.Data.Sqlite;
using WifiTester.Core.Models;

namespace WifiTester.Core.Storage;

public sealed class Repository : IDisposable
{
    private readonly SqliteConnection _conn;

    public Repository(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _conn.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        _conn.Execute(@"
CREATE TABLE IF NOT EXISTS wifi_samples(
  ts INTEGER, iface TEXT, state INTEGER, ssid TEXT, bssid TEXT,
  rssi INTEGER, quality INTEGER, band INTEGER, channel INTEGER,
  phy TEXT, tx INTEGER, rx INTEGER);
CREATE TABLE IF NOT EXISTS wifi_events(
  ts INTEGER, type INTEGER, from_bssid TEXT, to_bssid TEXT, reason TEXT);
CREATE TABLE IF NOT EXISTS latency_samples(
  ts INTEGER, target TEXT, rtt REAL, success INTEGER);
CREATE TABLE IF NOT EXISTS throughput_samples(
  ts INTEGER, down REAL, up REAL, server TEXT);
CREATE TABLE IF NOT EXISTS defects(
  ts_start INTEGER, ts_end INTEGER, type INTEGER, severity INTEGER,
  metric REAL, threshold REAL, ap_bssid TEXT, descr TEXT);
CREATE INDEX IF NOT EXISTS ix_wifi_ts ON wifi_samples(ts);
CREATE INDEX IF NOT EXISTS ix_lat_ts ON latency_samples(ts);");
    }

    private static long U(DateTimeOffset t) => t.ToUnixTimeMilliseconds();
    private static DateTimeOffset D(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

    public void SaveWifiSample(WifiSample s) => _conn.Execute(
        @"INSERT INTO wifi_samples VALUES(@ts,@iface,@state,@ssid,@bssid,@rssi,@quality,@band,@channel,@phy,@tx,@rx)",
        new { ts = U(s.Timestamp), iface = s.InterfaceName, state = (int)s.State, ssid = s.Ssid,
              bssid = s.Bssid, rssi = s.RssiDbm, quality = s.SignalQuality, band = (int)s.Band,
              channel = s.Channel, phy = s.PhyType, tx = s.TxRateMbps, rx = s.RxRateMbps });

    public void SaveWifiEvent(WifiEvent e) => _conn.Execute(
        @"INSERT INTO wifi_events VALUES(@ts,@type,@f,@t,@r)",
        new { ts = U(e.Timestamp), type = (int)e.Type, f = e.FromBssid, t = e.ToBssid, r = e.Reason });

    public void SaveLatencySample(LatencySample s) => _conn.Execute(
        @"INSERT INTO latency_samples VALUES(@ts,@target,@rtt,@ok)",
        new { ts = U(s.Timestamp), target = s.Target, rtt = s.RttMs, ok = s.Success ? 1 : 0 });

    public void SaveThroughputSample(ThroughputSample s) => _conn.Execute(
        @"INSERT INTO throughput_samples VALUES(@ts,@d,@u,@srv)",
        new { ts = U(s.Timestamp), d = s.DownMbps, u = s.UpMbps, srv = s.Server });

    public void SaveDefect(Defect d) => _conn.Execute(
        @"INSERT INTO defects VALUES(@s,@e,@type,@sev,@m,@th,@bssid,@descr)",
        new { s = U(d.Start), e = U(d.End), type = (int)d.Type, sev = (int)d.Severity,
              m = d.MetricValue, th = d.Threshold, bssid = d.ApBssid, descr = d.Description });

    public List<WifiSample> GetWifiSamples(DateTimeOffset from, DateTimeOffset to) =>
        _conn.Query("SELECT * FROM wifi_samples WHERE ts BETWEEN @a AND @b ORDER BY ts",
            new { a = U(from), b = U(to) })
        .Select(r => new WifiSample(D((long)r.ts), (string)r.iface, (WifiState)(int)(long)r.state,
            r.ssid as string, r.bssid as string, (int)(long)r.rssi, (int)(long)r.quality,
            (WifiBand)(int)(long)r.band, (int)(long)r.channel, r.phy as string,
            (int)(long)r.tx, (int)(long)r.rx)).ToList();

    public List<LatencySample> GetLatencySamples(DateTimeOffset from, DateTimeOffset to) =>
        _conn.Query("SELECT * FROM latency_samples WHERE ts BETWEEN @a AND @b ORDER BY ts",
            new { a = U(from), b = U(to) })
        .Select(r => new LatencySample(D((long)r.ts), (string)r.target, (double)r.rtt, (long)r.success == 1))
        .ToList();

    public List<Defect> GetDefects(DateTimeOffset from, DateTimeOffset to) =>
        _conn.Query("SELECT * FROM defects WHERE ts_start BETWEEN @a AND @b ORDER BY ts_start",
            new { a = U(from), b = U(to) })
        .Select(r => new Defect(D((long)r.ts_start), D((long)r.ts_end), (DefectType)(int)(long)r.type,
            (Severity)(int)(long)r.severity, (double)r.metric, (double)r.threshold,
            r.ap_bssid as string, (string)r.descr)).ToList();

    public void Purge(int retentionDays)
    {
        var cutoff = U(DateTimeOffset.Now.AddDays(-retentionDays));
        foreach (var (tbl, col) in new[] {
            ("wifi_samples","ts"), ("wifi_events","ts"), ("latency_samples","ts"),
            ("throughput_samples","ts"), ("defects","ts_start") })
            _conn.Execute($"DELETE FROM {tbl} WHERE {col} < @c", new { c = cutoff });
    }

    public void Dispose() => _conn.Dispose();
}
