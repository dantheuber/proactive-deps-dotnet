# proactive-deps for .NET

Lightweight, cached, proactive dependency health checks for .NET services.

Define async checks (DBs, REST APIs, queues, etc.), get structured status + latency, and expose Prometheus metrics (via Prometheus.Client) out-of-the-box.

## Features

- Simple registration of dependency checks
- Per-dependency TTL + refresh threshold (in-memory TTL cache with proactive refresh)
- Latency + health gauges for Prometheus (Prometheus.Client)
- Skippable checks (e.g., for local dev / disabled services)
- .NET-first API with records and async checks

## Install

Until published to NuGet, add a project reference to this library:

```bash
# From your app project folder
dotnet add reference ../path/to/src/ProactiveDeps/ProactiveDeps.csproj
```

Or clone this repository and use the provided sample.

## Quick Start

```csharp
using ProactiveDeps.Monitoring;

var monitor = new DependencyMonitor(new DependencyMonitorOptions
{
    // Optional defaults
    CacheDurationMs = 60_000,
    RefreshThresholdMs = 5_000,
    CheckIntervalMs = 15_000
});

monitor.Register(new DependencyCheckOptions
{
    Name = "redis",
    Description = "Redis cache",
    Impact = "Responses may be slower (cache miss path).",
    Check = async () =>
    {
        // Perform your health check here (ping Redis, open DB connection, etc.)
        await Task.Delay(5);
        return Constants.SUCCESS_STATUS_CODE; // 0
    },
    // Optional per-dependency overrides
    CacheDurationMs = 10_000,
    RefreshThresholdMs = 5_000,
    CheckDetails = new DatabaseCheckDetails("database", Server: "localhost", Database: "cache", DbType: "redis")
});

// Start periodic checks
monitor.StartDependencyCheckInterval();

// Query on-demand
var all = await monitor.GetAllStatuses();
var one = await monitor.GetStatus("redis");

// Render Prometheus text exposition
var metricsText = await monitor.GetPrometheusMetrics();
```

### Skipping a Dependency

```csharp
monitor.Register(new DependencyCheckOptions
{
    Name = "external-service",
    Description = "An external service that is temporarily disabled",
    Impact = "No impact since this service is currently unused.",
    Skip = true,
    Check = async () => Constants.SUCCESS_STATUS_CODE // won't run because Skip=true
});
```

### Return Shape

Check delegate returns either:
- `Constants.SUCCESS_STATUS_CODE | Constants.ERROR_STATUS_CODE | Constants.WARNING_STATUS_CODE` (int), or
- `new DependencyCheckResult { Code = ..., Error = ex, ErrorMessage = "..." }`

`Skip = true` short-circuits to an OK result with `latency: 0` and `skipped: true`.

### Fetch All Statuses

```csharp
var statuses = await monitor.GetAllStatuses();
// Example (C# record serialized to JSON):
// [
//   {
//     "name": "redis",
//     "description": "Redis cache layer",
//     "impact": "Responses may be slower due to missing cache.",
//     "healthy": true,
//     "health": {
//       "state": "OK",
//       "code": 0,
//       "latency": 5,
//       "skipped": false
//     },
//     "lastChecked": "2025-04-13T12:00:00Z"
//   }
// ]
```

### Single Dependency

```csharp
var status = await monitor.GetStatus("redis");
```

## Prometheus Metrics

The monitor initializes Prometheus.Client gauges on first use (or uses your provided registry):

- `dependency_latency_ms{dependency}` – last check latency (ms)
- `dependency_health{dependency,impact}` – health state (0 OK, 1 WARNING, 2 CRITICAL)

```csharp
var metrics = await monitor.GetPrometheusMetrics();
Console.WriteLine(metrics);
/*
# HELP dependency_latency_ms Last dependency check latency in milliseconds
# TYPE dependency_latency_ms gauge
dependency_latency_ms{dependency="redis"} 5

# HELP dependency_health Dependency health status (0=OK,1=WARNING,2=CRITICAL)
# TYPE dependency_health gauge
dependency_health{dependency="redis",impact="Responses may be slower (cache miss path)."} 0
*/
```

### ASP.NET Core Integration (optional)

Use `Prometheus.Client.AspNetCore` and `Prometheus.Client.DependencyInjection` to expose `/metrics` and share the same registry:

```csharp
// Program.cs / Startup.cs
using Prometheus.Client;
using Prometheus.Client.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMetricFactory(); // adds ICollectorRegistry & IMetricFactory

var app = builder.Build();
app.UsePrometheusServer(); // exposes /metrics

// Resolve the registry/factory to pass into DependencyMonitor
var registry = app.Services.GetRequiredService<ICollectorRegistry>();
var factory  = app.Services.GetRequiredService<IMetricFactory>();
var monitor  = new DependencyMonitor(new DependencyMonitorOptions { CheckIntervalMs = 15000 }, registry, factory);

monitor.Register(new DependencyCheckOptions { /* ... */ });
monitor.StartDependencyCheckInterval();

app.Run();
```

## Example Project (Console Demo)

See `samples/ConsoleDemo` for a minimal runnable example.

```bash
cd samples/ConsoleDemo
dotnet run
```

## API Overview

- `DependencyMonitor(options?, registry?, metricFactory?)`
  - `Register(DependencyCheckOptions)`
  - `StartDependencyCheckInterval()` / `StopDependencyCheckInterval()`
  - `Task<DependencyStatus> GetStatus(string name)`
  - `Task<IReadOnlyList<DependencyStatus>> GetAllStatuses()`
  - `Task<string> GetPrometheusMetrics()`
  - `ICollectorRegistry GetPrometheusRegistry()`
- `Constants`: status codes/messages and default timings
  - `SUCCESS_STATUS_CODE = 0`, `ERROR_STATUS_CODE = 1`, `WARNING_STATUS_CODE = 2`
  - `SUCCESS_STATUS_MESSAGE = "OK"`, `ERROR_STATUS_MESSAGE = "CRITICAL"`, `WARNING_STATUS_MESSAGE = "WARNING"`
  - Defaults: `DEFAULT_CACHE_DURATION_MS = 60000`, `DEFAULT_REFRESH_THRESHOLD_MS = 5000`, `DEFAULT_CHECK_INTERVAL_MS = 15000`
- `DependencyCheckOptions`: Name, Description, Impact, Skip, Contact, Check, CheckDetails, CacheDurationMs, RefreshThresholdMs
- `DependencyCheckResult`: Code, Error?, ErrorMessage?
- `DependencyStatus`: Name, Description, Impact, Contact?, Health { State, Code, Latency, Skipped }, Healthy, LastChecked, CheckDetails?, Error?, ErrorMessage?
- `CheckDetails` (optional records): `GenericCheckDetails`, `DatabaseCheckDetails`, `RestCheckDetails`, `SoapCheckDetails`

## License

MIT © 2025 Daniel Essig
