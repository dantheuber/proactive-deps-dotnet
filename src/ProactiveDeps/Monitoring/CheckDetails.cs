namespace ProactiveDeps.Monitoring;

// Optional structured details for a dependency check; consumers can extend/ignore
public interface ICheckDetails { string Type { get; } }

public record GenericCheckDetails(string Type, string? Notes = null) : ICheckDetails;

public record DatabaseCheckDetails(
    string Type,
    string Server,
    string? Database,
    string? DbType
) : ICheckDetails;

public record RestCheckDetails(
    string Type,
    string Url,
    string Method
) : ICheckDetails;

public record SoapCheckDetails(
    string Type,
    string Endpoint,
    string Action
) : ICheckDetails;
