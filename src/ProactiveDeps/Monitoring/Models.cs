using System.Diagnostics.CodeAnalysis;

namespace ProactiveDeps.Monitoring;

/// Options that control caching and scheduling behavior of DependencyMonitor.
/// If a value is null, a sensible default from <see cref="Constants"/> is used.
public record DependencyMonitorOptions(
    /// Default per-check cache duration in milliseconds.
    int? CacheDurationMs = null,
    /// Default refresh threshold in milliseconds. If time left <= threshold, a background refresh is triggered.
    int? RefreshThresholdMs = null,
    /// Interval in milliseconds for periodic checks when using StartDependencyCheckInterval.
    int? CheckIntervalMs = null,
    /// Reserved for future use. No effect currently.
    bool CollectDefaultMetrics = false
);

/// Options describing a dependency and how to evaluate its health.
public record DependencyCheckOptions
{
    /// Logical name of the dependency. Used as cache key and Prometheus label.
    public required string Name { get; init; }
    /// Human-readable description.
    public required string Description { get; init; }
    /// What happens to the system if this dependency degrades/fails. Also used as Prometheus label.
    public required string Impact { get; init; }
    /// If true, check is skipped and reported as OK with latency 0.
    public bool Skip { get; init; } = false;
    /// Optional contact map, e.g., {"slack":"#oncall"}.
    public IReadOnlyDictionary<string, string>? Contact { get; init; }
    /// Async delegate performing the health check and returning a result.
    public Func<Task<DependencyCheckResult>> Check { get; init; } = default!;
    /// Optional structured details describing the dependency under check.
    public ICheckDetails? CheckDetails { get; init; }
    /// Per-dependency override for cache duration (ms).
    public int? CacheDurationMs { get; init; }
    /// Per-dependency override for refresh threshold (ms).
    public int? RefreshThresholdMs { get; init; }
}

// Matches Node: either code or error details
/// Result returned by a dependency check.
public record DependencyCheckResult
{
    /// Status code: 0 OK, 1 CRITICAL, 2 WARNING.
    public int Code { get; init; }
    /// Optional exception encountered during the check.
    public Exception? Error { get; init; }
    /// Optional user-friendly error message.
    public string? ErrorMessage { get; init; }

    public static implicit operator DependencyCheckResult(int code) => new() { Code = code };
}

/// Structured status returned by the monitor for a dependency.
public record DependencyStatus
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Impact { get; init; }
    public IReadOnlyDictionary<string, string>? Contact { get; init; }
    public HealthBlock Health { get; init; } = new();
    public bool Healthy { get; init; }
    public string LastChecked { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public ICheckDetails? CheckDetails { get; init; }
    public ErrorBlock? Error { get; init; }
    public string? ErrorMessage { get; init; }

    /// Health details with normalized code and message.
    public record HealthBlock
    {
        public string State { get; init; } = Constants.ERROR_STATUS_MESSAGE;
        public int Code { get; init; } = Constants.ERROR_STATUS_CODE;
        public long Latency { get; init; } = 0;
        public bool Skipped { get; init; } = false;
    }

    /// Flattened error information (type, message, and stack).
    public record ErrorBlock
    {
        public required string Name { get; init; }
        public required string Message { get; init; }
        public string? Stack { get; init; }
    }
}
