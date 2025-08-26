using System.Diagnostics.CodeAnalysis;

namespace ProactiveDeps.Monitoring;

public record DependencyMonitorOptions(
    int? CacheDurationMs = null,
    int? RefreshThresholdMs = null,
    int? CheckIntervalMs = null,
    bool CollectDefaultMetrics = false
);

public record DependencyCheckOptions
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Impact { get; init; }
    public bool Skip { get; init; } = false;
    public IReadOnlyDictionary<string, string>? Contact { get; init; }
    public Func<Task<DependencyCheckResult>> Check { get; init; } = default!;
    public ICheckDetails? CheckDetails { get; init; }
    public int? CacheDurationMs { get; init; }
    public int? RefreshThresholdMs { get; init; }
}

// Matches Node: either code or error details
public record DependencyCheckResult
{
    public int Code { get; init; }
    public Exception? Error { get; init; }
    public string? ErrorMessage { get; init; }

    public static implicit operator DependencyCheckResult(int code) => new() { Code = code };
}

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

    public record HealthBlock
    {
        public string State { get; init; } = Constants.ERROR_STATUS_MESSAGE;
        public int Code { get; init; } = Constants.ERROR_STATUS_CODE;
        public long Latency { get; init; } = 0;
        public bool Skipped { get; init; } = false;
    }

    public record ErrorBlock
    {
        public required string Name { get; init; }
        public required string Message { get; init; }
        public string? Stack { get; init; }
    }
}
