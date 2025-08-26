namespace ProactiveDeps.Monitoring;

public static class Constants
{
    // Status codes
    public const int SUCCESS_STATUS_CODE = 0;
    public const int ERROR_STATUS_CODE = 1;
    public const int WARNING_STATUS_CODE = 2;

    // Status messages
    public const string SUCCESS_STATUS_MESSAGE = "OK";
    public const string ERROR_STATUS_MESSAGE = "CRITICAL";
    public const string WARNING_STATUS_MESSAGE = "WARNING";

    // Defaults
    public const int DEFAULT_CACHE_DURATION_MS = 60_000; // 1 minute
    public const int DEFAULT_REFRESH_THRESHOLD_MS = 5_000; // 5 seconds
    public const int DEFAULT_CHECK_INTERVAL_MS = 15_000; // 15 seconds
}
