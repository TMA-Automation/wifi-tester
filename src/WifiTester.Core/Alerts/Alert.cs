using WifiTester.Core.Models;

namespace WifiTester.Core.Alerts;

public record Alert(DateTimeOffset Timestamp, Severity Severity, string Title, string Message);
