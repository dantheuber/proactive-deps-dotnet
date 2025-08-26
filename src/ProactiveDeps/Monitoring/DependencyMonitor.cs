using Prometheus.Client;
using Prometheus.Client.Collectors;
using System;
using System.Collections.Concurrent;

namespace ProactiveDeps.Monitoring;

/// Simple in-memory cache with TTL and refresh threshold semantics, inspired by cache-manager
internal sealed class TtlCache
{
    private sealed record Entry(object Value, DateTimeOffset ExpiresAt, TimeSpan Ttl, TimeSpan RefreshThreshold)
    {
        public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
        public bool ShouldRefresh(DateTimeOffset now) => (ExpiresAt - now) <= RefreshThreshold;
    }

    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public async Task<T> Wrap<T>(string key, Func<Task<T>> factory, TimeSpan ttl, TimeSpan refreshThreshold)
    {
        var now = DateTimeOffset.UtcNow;
        if (_store.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired(now))
            {
                if (entry.ShouldRefresh(now))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var v = await factory().ConfigureAwait(false);
                            Set(key, v!, ttl, refreshThreshold);
                        }
                        catch { /* ignore background refresh errors */ }
                    });
                }
                return (T)entry.Value;
            }
        }

        var value = await factory().ConfigureAwait(false);
        Set(key, value!, ttl, refreshThreshold);
        return value;
    }

    public T? Get<T>(string key)
    {
        var now = DateTimeOffset.UtcNow;
        if (_store.TryGetValue(key, out var entry) && !entry.IsExpired(now))
        {
            return (T)entry.Value;
        }
        return default;
    }

    public void Set<T>(string key, T value, TimeSpan ttl, TimeSpan refreshThreshold)
    {
        var expires = DateTimeOffset.UtcNow.Add(ttl);
        var e = new Entry(value!, expires, ttl, refreshThreshold);
        _store[key] = e;
    }
}

public sealed class DependencyMonitor
{
    private readonly List<DependencyCheckOptions> _dependencies = new();
    private readonly TtlCache _cache = new();
    private readonly int _refreshThresholdMs;
    private readonly int _cacheDurationMs;
    private readonly int _checkIntervalMs;

    // Prometheus
    private readonly ICollectorRegistry _registry;
    private readonly IMetricFactory _metrics;
    private readonly IMetricFamily<IGauge> _latencyGauge;
    private readonly IMetricFamily<IGauge> _healthGauge;

    private Timer? _timer;

    public bool CheckIntervalStarted { get; private set; }

    public DependencyMonitor(DependencyMonitorOptions? options = null, ICollectorRegistry? registry = null, IMetricFactory? metricFactory = null)
    {
        options ??= new();
        _cacheDurationMs = options.CacheDurationMs ?? Constants.DEFAULT_CACHE_DURATION_MS;
        _refreshThresholdMs = options.RefreshThresholdMs ?? Constants.DEFAULT_REFRESH_THRESHOLD_MS;
        _checkIntervalMs = options.CheckIntervalMs ?? Constants.DEFAULT_CHECK_INTERVAL_MS;

    _registry = registry ?? new CollectorRegistry();
    _metrics = metricFactory ?? new MetricFactory(_registry);

        // Gauges: dependency_latency_ms{dependency} and dependency_health{dependency,impact}
        _latencyGauge = _metrics.CreateGauge(
            "dependency_latency_ms",
            "Last dependency check latency in milliseconds",
            labelNames: new[] { "dependency" }
        );
        _healthGauge = _metrics.CreateGauge(
            "dependency_health",
            "Dependency health status (0=OK,1=WARNING,2=CRITICAL)",
            labelNames: new[] { "dependency", "impact" }
        );
        // Note: Collecting default process metrics is provided by extensions; caller can wire if needed
    }

    public void Register(DependencyCheckOptions dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        if (string.IsNullOrWhiteSpace(dependency.Name)) throw new ArgumentException("Dependency name is required", nameof(dependency));
        if (dependency.Check is null) throw new ArgumentException("Dependency check delegate is required", nameof(dependency));
        _dependencies.Add(dependency);
    }

    public void StartDependencyCheckInterval()
    {
        CheckIntervalStarted = true;
        _timer?.Dispose();
        _ = GetAllStatuses(); // trigger initial
        _timer = new Timer(async _ =>
        {
            try { await GetAllStatuses().ConfigureAwait(false); } catch { /* swallow */ }
        }, null, _checkIntervalMs, _checkIntervalMs);
    }

    public void StopDependencyCheckInterval()
    {
        CheckIntervalStarted = false;
        _timer?.Dispose();
        _timer = null;
    }

    public async Task<DependencyStatus> GetStatus(string dependencyName)
    {
        var cached = _cache.Get<DependencyStatus>(dependencyName);
        if (cached is not null) return cached;

        var dep = _dependencies.FirstOrDefault(d => d.Name == dependencyName)
            ?? throw new InvalidOperationException($"Dependency {dependencyName} not found");
        return await GetDependencyStatus(dep).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DependencyStatus>> GetAllStatuses()
    {
        var tasks = _dependencies.Select(GetDependencyStatus).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    public async Task<string> GetPrometheusMetrics()
    {
        // ensure gauges updated prior to scrape
        _ = await GetAllStatuses().ConfigureAwait(false);

        using var ms = new MemoryStream();
        await Prometheus.Client.ScrapeHandler.ProcessAsync(_registry, ms).ConfigureAwait(false);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    public ICollectorRegistry GetPrometheusRegistry() => _registry;

    private async Task<DependencyStatus> GetDependencyStatus(DependencyCheckOptions dependency)
    {
        var ttl = TimeSpan.FromMilliseconds(dependency.CacheDurationMs ?? _cacheDurationMs);
        var refresh = TimeSpan.FromMilliseconds(dependency.RefreshThresholdMs ?? _refreshThresholdMs);

        try
        {
            return await _cache.Wrap(
                dependency.Name,
                async () =>
                {
                    if (dependency.Skip)
                    {
                        var skipped = FormatCheckResult(dependency, new DependencyCheckResult { Code = Constants.SUCCESS_STATUS_CODE }, 0, skipped: true);
                        UpdateMetrics(skipped);
                        return skipped;
                    }

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var res = await dependency.Check().ConfigureAwait(false);
                    sw.Stop();
                    var status = FormatCheckResult(dependency, res, sw.ElapsedMilliseconds, skipped: false);
                    UpdateMetrics(status);
                    return status;
                },
                ttl,
                refresh
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failure = FormatCheckResult(dependency, new DependencyCheckResult { Code = Constants.ERROR_STATUS_CODE, Error = ex, ErrorMessage = $"Error checking dependency {dependency.Name}" });
            _cache.Set(dependency.Name, failure, ttl, refresh);
            UpdateMetrics(failure);
            return failure;
        }
    }

    private static DependencyStatus FormatCheckResult(DependencyCheckOptions dep, DependencyCheckResult result, long latencyMs = 0, bool skipped = false)
    {
        int resultCode = result.Code;
        var status = new DependencyStatus
        {
            Name = dep.Name,
            Description = dep.Description,
            Impact = dep.Impact,
            Contact = dep.Contact,
            CheckDetails = dep.CheckDetails,
            Healthy = false,
            Health = new DependencyStatus.HealthBlock
            {
                Code = Constants.ERROR_STATUS_CODE,
                State = Constants.ERROR_STATUS_MESSAGE,
                Latency = latencyMs,
                Skipped = false
            }
        };

        if (skipped)
        {
            status = status with
            {
                Healthy = true,
                Health = status.Health with { Code = Constants.SUCCESS_STATUS_CODE, State = Constants.SUCCESS_STATUS_MESSAGE, Latency = 0, Skipped = true }
            };
            return status;
        }

        switch (resultCode)
        {
            case Constants.SUCCESS_STATUS_CODE:
                status = status with
                {
                    Healthy = true,
                    Health = status.Health with { Code = Constants.SUCCESS_STATUS_CODE, State = Constants.SUCCESS_STATUS_MESSAGE, Latency = latencyMs }
                };
                break;
            case Constants.WARNING_STATUS_CODE:
                status = status with
                {
                    Healthy = true,
                    Health = status.Health with { Code = Constants.WARNING_STATUS_CODE, State = Constants.WARNING_STATUS_MESSAGE, Latency = latencyMs }
                };
                break;
            case Constants.ERROR_STATUS_CODE:
            default:
                if (result.Error is not null)
                {
                    status = status with
                    {
                        Error = new DependencyStatus.ErrorBlock { Name = result.Error.GetType().Name, Message = result.Error.Message, Stack = result.Error.StackTrace },
                        ErrorMessage = result.ErrorMessage ?? status.ErrorMessage
                    };
                }
                else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    status = status with { ErrorMessage = result.ErrorMessage };
                }
                status = status with { Health = status.Health with { Latency = latencyMs } };
                break;
        }

        return status;
    }

    private void UpdateMetrics(DependencyStatus status)
    {
        var value = status.Health.State switch
        {
            var s when s == Constants.SUCCESS_STATUS_MESSAGE => 0,
            var s when s == Constants.WARNING_STATUS_MESSAGE => 1,
            _ => 2
        };

    _latencyGauge.WithLabels(status.Name).Set(status.Health.Latency);
    _healthGauge.WithLabels(status.Name, status.Impact).Set(value);
    }
}
