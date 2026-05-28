using System.Text;
using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Monitoring;
using WifiTester.Core.Probing;
using WifiTester.Core.Reporting;
using WifiTester.Core.Storage;
using WifiTester.Host;

Console.OutputEncoding = Encoding.UTF8;

var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WifiTester");
Directory.CreateDirectory(dir);
var configPath = Path.Combine(dir, "config.json");
var cfg = MonitorConfig.Load(configPath);
cfg.Save(configPath);

using var repo = new Repository(Path.Combine(dir, "wifitester.db"));

if (args.Length > 0 && args[0] == "report")
{
    var to = DateTimeOffset.Now;
    var from = to.AddDays(-1);
    var wifi = repo.GetWifiSamples(from, to);
    var defects = repo.GetDefects(from, to);
    var data = ReportData.Build(from, to, wifi, defects);
    var html = HtmlReportGenerator.Generate(data);
    var outPath = Path.Combine(dir, $"raport_{to:yyyyMMdd_HHmm}.html");
    File.WriteAllText(outPath, html);
    File.WriteAllText(Path.ChangeExtension(outPath, ".csv"), CsvExporter.DefectsToCsv(defects));
    File.WriteAllBytes(Path.ChangeExtension(outPath, ".pdf"), PdfReportGenerator.Generate(data));
    Console.WriteLine($"Raport zapisany: {outPath}");
    return;
}

// "gateway" -> wykryj bramę domyślną
cfg.PingTargets = cfg.PingTargets
    .Select(t => t == "gateway" ? (GatewayFinder.Get() ?? "127.0.0.1") : t).ToList();

var svc = new MonitoringService(cfg, new NetshWifiSource(), new PingNetworkProbe(),
    new HttpThroughputTester(cfg.ThroughputUrl), new SystemClock());

var lastPurge = DateTimeOffset.MinValue;
svc.WifiSampleCollected += (_, s) => repo.SaveWifiSample(s);
svc.WifiEventDetected += (_, e) => repo.SaveWifiEvent(e);
svc.LatencyCollected += (_, l) => repo.SaveLatencySample(l);
svc.ThroughputCollected += (_, t) => repo.SaveThroughputSample(t);
svc.DefectDetected += (_, d) =>
{
    repo.SaveDefect(d);
    Console.WriteLine($"[DEFEKT] {d.Type} {d.Severity}: {d.Description}");
};
svc.WifiSampleCollected += (_, _) =>
{
    if (DateTimeOffset.Now - lastPurge > TimeSpan.FromHours(1))
    {
        repo.Purge(cfg.RetentionDays);
        lastPurge = DateTimeOffset.Now;
    }
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await svc.RunAsync(cts.Token);
