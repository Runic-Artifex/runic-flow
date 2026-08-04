using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicFlow;

/// <summary>Identifies the effect of a registration diagnostic.</summary>
public enum FlowRegistrationDiagnosticSeverity
{
    /// <summary>Informational validation output.</summary>
    Info,

    /// <summary>A non-fatal registration concern.</summary>
    Warning,

    /// <summary>A registration error that prevents startup.</summary>
    Error,
}

/// <summary>Represents deterministic registry or adapter validation output.</summary>
public sealed record FlowRegistrationDiagnostic
{
    /// <summary>Initializes a diagnostic.</summary>
    public FlowRegistrationDiagnostic(
        string id,
        FlowRegistrationDiagnosticSeverity severity,
        string message,
        FlowRegistrationIdentity? identity = null,
        FlowRegistrationLocation location = default,
        FlowRegistrationLocation relatedLocation = default)
    {
        Id = ValidateDiagnosticId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        Severity = severity;
        Message = message;
        Identity = identity;
        Location = location;
        RelatedLocation = relatedLocation;
    }

    /// <summary>Gets the stable RFLOW identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the diagnostic severity.</summary>
    public FlowRegistrationDiagnosticSeverity Severity { get; }

    /// <summary>Gets the deterministic diagnostic message.</summary>
    public string Message { get; }

    /// <summary>Gets the implicated registration, when present.</summary>
    public FlowRegistrationIdentity? Identity { get; }

    /// <summary>Gets the primary source location.</summary>
    public FlowRegistrationLocation Location { get; }

    /// <summary>Gets a related source location, such as the first duplicate.</summary>
    public FlowRegistrationLocation RelatedLocation { get; }

    internal static FlowRegistrationDiagnostic Duplicate(
        FlowRegistration registration,
        FlowRegistration first) =>
        new(
            FlowDiagnosticIds.DuplicateLogicalKey,
            FlowRegistrationDiagnosticSeverity.Error,
            $"Duplicate Flow registration '{registration.Identity}'. " +
            $"First registration: {first.Location}; duplicate: {registration.Location}.",
            registration.Identity,
            registration.Location,
            first.Location);

    private static string ValidateDiagnosticId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length != 9 || !id.StartsWith("RFLOW", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A Flow diagnostic identifier must be in the reserved RFLOW0001-RFLOW0999 range.",
                nameof(id));
        }

        int numericId = 0;
        for (int index = 5; index < id.Length; index++)
        {
            if (id[index] is < '0' or > '9')
            {
                throw new ArgumentException(
                    "A Flow diagnostic identifier must be in the reserved RFLOW0001-RFLOW0999 range.",
                    nameof(id));
            }

            numericId = (numericId * 10) + (id[index] - '0');
        }

        if (numericId is < 1 or > 999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "A Flow diagnostic identifier must be in the reserved RFLOW0001-RFLOW0999 range.");
        }

        return id;
    }
}

/// <summary>Collects diagnostics emitted by one deterministic validator.</summary>
public sealed class FlowRegistrationDiagnosticSink
{
    private readonly List<FlowRegistrationDiagnostic> _diagnostics = [];

    /// <summary>Reports one diagnostic.</summary>
    public void Report(FlowRegistrationDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnostics.Add(diagnostic);
    }

    internal IReadOnlyList<FlowRegistrationDiagnostic> Snapshot() =>
        new ReadOnlyCollection<FlowRegistrationDiagnostic>(_diagnostics.ToArray());
}

/// <summary>Validates a frozen registry without mutating it.</summary>
/// <param name="context">The immutable registration and adapter mapping context.</param>
/// <param name="diagnostics">The diagnostic sink.</param>
public delegate void FlowRegistryValidator(
    FlowRegistryValidationContext context,
    FlowRegistrationDiagnosticSink diagnostics);
