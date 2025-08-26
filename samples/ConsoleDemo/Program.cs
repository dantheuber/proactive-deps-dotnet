using ProactiveDeps.Monitoring;

var monitor = new DependencyMonitor(new DependencyMonitorOptions
{
	CacheDurationMs = 10_000,
	RefreshThresholdMs = 2_000,
	CheckIntervalMs = 5_000
});

monitor.Register(new DependencyCheckOptions
{
	Name = "redis",
	Description = "Redis cache",
	Impact = "Responses may be slower (cache miss path).",
	Check = async () =>
	{
		await Task.Delay(5);
		return Constants.SUCCESS_STATUS_CODE;
	},
	CheckDetails = new DatabaseCheckDetails("database", Server: "localhost", Database: "cache", DbType: "redis")
});

monitor.Register(new DependencyCheckOptions
{
	Name = "external-service",
	Description = "Skipped dependency",
	Impact = "No impact",
	Skip = true,
	Check = async () => Constants.SUCCESS_STATUS_CODE
});

monitor.StartDependencyCheckInterval();

Console.WriteLine("Running checks for 1 interval...");
await Task.Delay(6_000);

var all = await monitor.GetAllStatuses();
foreach (var s in all)
{
	Console.WriteLine($"{s.Name} healthy={s.Healthy} code={s.Health.Code} latency={s.Health.Latency} skipped={s.Health.Skipped}");
}

Console.WriteLine("\nPrometheus metrics:\n");
var metrics = await monitor.GetPrometheusMetrics();
Console.WriteLine(metrics);
