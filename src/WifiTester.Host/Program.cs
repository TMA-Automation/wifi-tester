using WifiTester.Core.Abstractions;
using WifiTester.Core.Config;
using WifiTester.Core.Probing;
using WifiTester.Core.Reporting;
using WifiTester.Core.Storage;
using WifiTester.Host;

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
    Console.WriteLine($"Raport zapisany: {outPath}");
    return;
}

// "gateway" -> wykryj bramę domyślną
cfg.PingTargets = cfg.PingTargets
    .Select(t => t == "gateway" ? (GatewayFinder.Get() ?? "127.0.0.1") : t).ToList();

var loop = new MonitorLoop(cfg, new NetshWifiSource(), new PingNetworkProbe(),
    new HttpThroughputTester(cfg.ThroughputUrl), repo, new SystemClock());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await loop.RunAsync(cts.Token);
