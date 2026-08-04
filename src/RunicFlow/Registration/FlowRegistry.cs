using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace RunicFlow;

/// <summary>Composes explicit registrations and freezes them into an immutable snapshot.</summary>
/// <remarks>Builder mutation is intentionally single-threaded during application composition.</remarks>
public sealed class FlowRegistryBuilder
{
    private readonly Dictionary<FlowRegistrationIdentity, FlowRegistration> _registrations = [];
    private readonly Dictionary<FlowAdapterMappingIdentity, FlowAdapterMapping> _adapterMappings = [];
    private readonly Dictionary<string, ValidatorEntry> _validators = new(StringComparer.Ordinal);
    private FlowRegistrySnapshot? _snapshot;
    private IReadOnlyList<FlowRegistrationDiagnostic>? _diagnostics;
    private bool _frozen;

    /// <summary>Gets whether registration mutation has been permanently disabled.</summary>
    public bool IsFrozen => _frozen;

    /// <summary>Adds an explicit Flow registration.</summary>
    /// <exception cref="FlowRegistrationException">The key is duplicated or the builder is frozen.</exception>
    public void Add(FlowRegistration registration)
    {
        if (!TryAdd(registration, out FlowRegistrationDiagnostic? diagnostic))
        {
            throw new FlowRegistrationException(
                $"{diagnostic!.Id}: {diagnostic.Message}",
                registration.Identity.LogicalKey);
        }
    }

    /// <summary>Attempts to add a registration and returns a structured duplicate diagnostic.</summary>
    public bool TryAdd(
        FlowRegistration registration,
        out FlowRegistrationDiagnostic? diagnostic)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(registration);

        if (_registrations.TryGetValue(registration.Identity, out FlowRegistration? first))
        {
            diagnostic = FlowRegistrationDiagnostic.Duplicate(registration, first);
            return false;
        }

        _registrations.Add(registration.Identity, registration);
        diagnostic = null;
        return true;
    }

    /// <summary>Adds one adapter-provided mapping declaration.</summary>
    /// <exception cref="FlowRegistrationException">The mapping is duplicated or the builder is frozen.</exception>
    public void AddAdapterMapping(FlowAdapterMapping mapping)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(mapping);

        if (_adapterMappings.TryGetValue(mapping.Identity, out FlowAdapterMapping? first))
        {
            throw new FlowRegistrationException(
                $"{FlowDiagnosticIds.DuplicateLogicalKey}: Duplicate adapter mapping " +
                $"'{mapping.Identity}'. First registration: {first.Location}; " +
                $"duplicate: {mapping.Location}.",
                mapping.Identity.LogicalKey);
        }

        _adapterMappings.Add(mapping.Identity, mapping);
    }

    /// <summary>Adds a named validator. Validators execute by ordinal name, not registration order.</summary>
    public void AddValidator(
        string name,
        FlowRegistryValidator validator,
        FlowRegistrationLocation location = default)
    {
        ThrowIfFrozen();
        string validatedName = FlowKey.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(validator);

        if (_validators.TryGetValue(validatedName, out ValidatorEntry? first))
        {
            throw new FlowRegistrationException(
                $"{FlowDiagnosticIds.DuplicateLogicalKey}: Duplicate Flow validator " +
                $"'{validatedName}'. First registration: {first.Location}; duplicate: {location}.",
                validatedName);
        }

        _validators.Add(validatedName, new ValidatorEntry(validatedName, validator, location));
    }

    /// <summary>
    /// Freezes registrations and validates the immutable snapshot.
    /// </summary>
    /// <exception cref="FlowValidationException">One or more error diagnostics were reported.</exception>
    public FlowRegistrySnapshot Freeze()
    {
        FlowRegistrySnapshot snapshot = Freeze(out IReadOnlyList<FlowRegistrationDiagnostic> diagnostics);
        if (ContainsErrors(diagnostics))
        {
            throw new FlowValidationException(CreateValidationMessage(diagnostics));
        }

        return snapshot;
    }

    /// <summary>
    /// Freezes registrations and returns all diagnostics in deterministic order without throwing.
    /// </summary>
    /// <remarks>
    /// The builder remains frozen even when validation reports errors. Repeated calls return the
    /// same immutable snapshot and diagnostic values.
    /// </remarks>
    public FlowRegistrySnapshot Freeze(
        out IReadOnlyList<FlowRegistrationDiagnostic> diagnostics)
    {
        if (_snapshot is not null)
        {
            diagnostics = _diagnostics!;
            return _snapshot;
        }

        _frozen = true;
        FlowRegistration[] registrations = [.. _registrations.Values];
        Array.Sort(registrations, FlowRegistrationComparer.Instance);
        FlowAdapterMapping[] mappings = [.. _adapterMappings.Values];
        Array.Sort(mappings, FlowAdapterMappingComparer.Instance);

        FlowRegistrySnapshot snapshot = new(registrations, mappings);
        ValidatorEntry[] validators = [.. _validators.Values];
        Array.Sort(validators, ValidatorEntryComparer.Instance);

        FlowRegistrationDiagnosticSink sink = new();
        FlowRegistryValidationContext context = new(snapshot);
        foreach (ValidatorEntry entry in validators)
        {
            try
            {
                entry.Validator(context, sink);
            }
            catch (Exception exception) when (exception is not FlowException)
            {
                throw new FlowValidationException(
                    $"Flow validator '{entry.Name}' failed at {entry.Location}.",
                    entry.Name,
                    exception);
            }
        }

        FlowRegistrationDiagnostic[] orderedDiagnostics = [.. sink.Snapshot()];
        Array.Sort(orderedDiagnostics, FlowRegistrationDiagnosticComparer.Instance);
        _snapshot = snapshot;
        _diagnostics = new ReadOnlyCollection<FlowRegistrationDiagnostic>(orderedDiagnostics);
        diagnostics = _diagnostics;
        return snapshot;
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new FlowRegistrationException(
                "Flow registrations have been frozen and cannot be mutated.");
        }
    }

    private static bool ContainsErrors(IReadOnlyList<FlowRegistrationDiagnostic> diagnostics)
    {
        foreach (FlowRegistrationDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == FlowRegistrationDiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateValidationMessage(IReadOnlyList<FlowRegistrationDiagnostic> diagnostics)
    {
        StringBuilder message = new("Flow registration validation failed:");
        foreach (FlowRegistrationDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == FlowRegistrationDiagnosticSeverity.Error)
            {
                message.Append('\n');
                message.Append(diagnostic.Id);
                message.Append(": ");
                message.Append(diagnostic.Message);
            }
        }

        return message.ToString();
    }

    private sealed record ValidatorEntry(
        string Name,
        FlowRegistryValidator Validator,
        FlowRegistrationLocation Location);

    private sealed class ValidatorEntryComparer : IComparer<ValidatorEntry>
    {
        public static readonly ValidatorEntryComparer Instance = new();

        public int Compare(ValidatorEntry? x, ValidatorEntry? y) =>
            string.CompareOrdinal(x?.Name, y?.Name);
    }
}

/// <summary>Exposes immutable, deterministically ordered application metadata.</summary>
public sealed class FlowRegistrySnapshot
{
    private readonly Dictionary<FlowRegistrationIdentity, FlowRegistration> _registrations;
    private readonly Dictionary<FlowAdapterMappingIdentity, FlowAdapterMapping> _adapterMappings;

    internal FlowRegistrySnapshot(
        FlowRegistration[] registrations,
        FlowAdapterMapping[] adapterMappings)
    {
        Registrations = new ReadOnlyCollection<FlowRegistration>(registrations);
        AdapterMappings = new ReadOnlyCollection<FlowAdapterMapping>(adapterMappings);
        _registrations = new Dictionary<FlowRegistrationIdentity, FlowRegistration>(registrations.Length);
        _adapterMappings = new Dictionary<FlowAdapterMappingIdentity, FlowAdapterMapping>(adapterMappings.Length);

        foreach (FlowRegistration registration in registrations)
        {
            _registrations.Add(registration.Identity, registration);
        }

        foreach (FlowAdapterMapping mapping in adapterMappings)
        {
            _adapterMappings.Add(mapping.Identity, mapping);
        }
    }

    /// <summary>Gets registrations ordered by kind and then logical key.</summary>
    public IReadOnlyList<FlowRegistration> Registrations { get; }

    /// <summary>Gets adapter mappings ordered by kind, logical key, and adapter name.</summary>
    public IReadOnlyList<FlowAdapterMapping> AdapterMappings { get; }

    /// <summary>Finds a registration by composite identity.</summary>
    public bool TryGetRegistration(
        FlowRegistrationKind kind,
        string logicalKey,
        out FlowRegistration? registration) =>
        _registrations.TryGetValue(new FlowRegistrationIdentity(kind, logicalKey), out registration);

    /// <summary>Finds an adapter mapping by composite identity.</summary>
    public bool TryGetAdapterMapping(
        FlowAdapterMappingKind kind,
        string logicalKey,
        out FlowAdapterMapping? mapping) =>
        _adapterMappings.TryGetValue(new FlowAdapterMappingIdentity(kind, logicalKey), out mapping);
}

/// <summary>Supplies immutable registries to core and adapter validation.</summary>
public sealed class FlowRegistryValidationContext
{
    internal FlowRegistryValidationContext(FlowRegistrySnapshot registry)
    {
        Registry = registry;
    }

    /// <summary>Gets the frozen application registry.</summary>
    public FlowRegistrySnapshot Registry { get; }

    /// <summary>Gets whether an installed adapter reported a mapping.</summary>
    public bool HasAdapterMapping(FlowAdapterMappingKind kind, string logicalKey) =>
        Registry.TryGetAdapterMapping(kind, logicalKey, out _);
}

internal sealed class FlowRegistrationComparer : IComparer<FlowRegistration>
{
    public static readonly FlowRegistrationComparer Instance = new();

    public int Compare(FlowRegistration? x, FlowRegistration? y)
    {
        int kind = Nullable.Compare(x?.Identity.Kind, y?.Identity.Kind);
        return kind != 0
            ? kind
            : string.CompareOrdinal(x?.Identity.LogicalKey, y?.Identity.LogicalKey);
    }
}

internal sealed class FlowAdapterMappingComparer : IComparer<FlowAdapterMapping>
{
    public static readonly FlowAdapterMappingComparer Instance = new();

    public int Compare(FlowAdapterMapping? x, FlowAdapterMapping? y)
    {
        int kind = Nullable.Compare(x?.Identity.Kind, y?.Identity.Kind);
        if (kind != 0)
        {
            return kind;
        }

        int key = string.CompareOrdinal(x?.Identity.LogicalKey, y?.Identity.LogicalKey);
        return key != 0 ? key : string.CompareOrdinal(x?.AdapterName, y?.AdapterName);
    }
}

internal sealed class FlowRegistrationDiagnosticComparer : IComparer<FlowRegistrationDiagnostic>
{
    public static readonly FlowRegistrationDiagnosticComparer Instance = new();

    public int Compare(FlowRegistrationDiagnostic? x, FlowRegistrationDiagnostic? y)
    {
        int id = string.CompareOrdinal(x?.Id, y?.Id);
        if (id != 0)
        {
            return id;
        }

        int kind = Nullable.Compare(x?.Identity?.Kind, y?.Identity?.Kind);
        if (kind != 0)
        {
            return kind;
        }

        int key = string.CompareOrdinal(x?.Identity?.LogicalKey, y?.Identity?.LogicalKey);
        if (key != 0)
        {
            return key;
        }

        int location = string.CompareOrdinal(x?.Location.ToString(), y?.Location.ToString());
        return location != 0 ? location : string.CompareOrdinal(x?.Message, y?.Message);
    }
}
