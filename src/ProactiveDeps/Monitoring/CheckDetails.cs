namespace ProactiveDeps.Monitoring;

/// Optional structured details for a dependency check; consumers can extend/ignore.
public interface ICheckDetails { string Type { get; } }

/// Unstructured or free-form check details.
public record GenericCheckDetails(string Type, string? Notes = null) : ICheckDetails;

/// Details for a database health check.
public record DatabaseCheckDetails(
    string Type,
    string Server,
    string? Database,
    string? DbType
) : ICheckDetails;

/// Details for a REST endpoint health check.
public record RestCheckDetails(
    string Type,
    string Url,
    string Method
) : ICheckDetails;

/// Details for a SOAP endpoint health check.
public record SoapCheckDetails(
    string Type,
    string Endpoint,
    string Action
) : ICheckDetails;
