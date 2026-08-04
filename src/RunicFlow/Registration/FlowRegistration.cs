using System;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow;

/// <summary>Identifies a registry partition while keeping public feature namespaces flat.</summary>
public enum FlowRegistrationKind
{
    /// <summary>A navigation region definition.</summary>
    NavigationRegion,

    /// <summary>A closed navigation route.</summary>
    NavigationRoute,

    /// <summary>A closed dialog conversation.</summary>
    Dialog,

    /// <summary>An operation policy or presentation definition.</summary>
    Operation,

    /// <summary>A workflow definition.</summary>
    Workflow,

    /// <summary>A closed workflow step.</summary>
    WorkflowStep,
}

/// <summary>Identifies one registration using ordinal, case-sensitive key semantics.</summary>
public readonly record struct FlowRegistrationIdentity
{
    /// <summary>Initializes a composite registration identity.</summary>
    public FlowRegistrationIdentity(FlowRegistrationKind kind, string logicalKey)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        LogicalKey = FlowKey.Validate(logicalKey, nameof(logicalKey));
        Kind = kind;
    }

    /// <summary>Gets the registry partition.</summary>
    public FlowRegistrationKind Kind { get; }

    /// <summary>Gets the logical key.</summary>
    public string LogicalKey { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{LogicalKey}";
}

/// <summary>Describes an optional deterministic source location for diagnostics.</summary>
public readonly record struct FlowRegistrationLocation
{
    /// <summary>Initializes a source location.</summary>
    /// <param name="source">A stable source label, such as a project-relative path.</param>
    /// <param name="line">A one-based line, or zero when unavailable.</param>
    /// <param name="column">A one-based column, or zero when unavailable.</param>
    public FlowRegistrationLocation(string? source, int line = 0, int column = 0)
    {
        if (source is { Length: 0 })
        {
            throw new ArgumentException("A registration source label cannot be empty.", nameof(source));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        if (line == 0 && column != 0)
        {
            throw new ArgumentException("A registration column requires a line.", nameof(column));
        }

        Source = source;
        Line = line;
        Column = column;
    }

    /// <summary>Gets a location with no source information.</summary>
    public static FlowRegistrationLocation Unknown => default;

    /// <summary>Gets the stable source label, when available.</summary>
    public string? Source { get; }

    /// <summary>Gets the one-based line, or zero when unavailable.</summary>
    public int Line { get; }

    /// <summary>Gets the one-based column, or zero when unavailable.</summary>
    public int Column { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        if (Source is null)
        {
            return "<unknown>";
        }

        return Line == 0
            ? Source
            : Column == 0
                ? $"{Source}({Line})"
                : $"{Source}({Line},{Column})";
    }
}

/// <summary>
/// Stores a closed, AOT-safe Flow registration and its declared type contract.
/// </summary>
public sealed class FlowRegistration
{
    private readonly Func<FlowActivationScope, CancellationToken, ValueTask<object>>? _factory;

    private FlowRegistration(
        FlowRegistrationIdentity identity,
        ViewContract? contract,
        Type? viewModelType,
        Type? parameterType,
        Type? resultType,
        Func<FlowActivationScope, CancellationToken, ValueTask<object>>? factory,
        FlowRegistrationLocation location)
    {
        Identity = identity;
        Contract = contract;
        ViewModelType = viewModelType;
        ParameterType = parameterType;
        ResultType = resultType;
        _factory = factory;
        Location = location;
    }

    /// <summary>Gets the composite identity.</summary>
    public FlowRegistrationIdentity Identity { get; }

    /// <summary>Gets the presentation contract, when this registration activates content.</summary>
    public ViewContract? Contract { get; }

    /// <summary>Gets the declared closed ViewModel type, when content is activated.</summary>
    public Type? ViewModelType { get; }

    /// <summary>Gets the declared closed parameter or request type, when present.</summary>
    public Type? ParameterType { get; }

    /// <summary>Gets the declared closed result type, when present.</summary>
    public Type? ResultType { get; }

    /// <summary>Gets the source location.</summary>
    public FlowRegistrationLocation Location { get; }

    /// <summary>Gets whether this registration has an explicit closed content factory.</summary>
    public bool HasFactory => _factory is not null;

    /// <summary>Creates metadata that does not activate content, such as a region or operation policy.</summary>
    public static FlowRegistration CreateMetadata(
        FlowRegistrationKind kind,
        string logicalKey,
        FlowRegistrationLocation location = default) =>
        new(new FlowRegistrationIdentity(kind, logicalKey), null, null, null, null, null, location);

    /// <summary>Creates a closed content registration with no parameter or result type.</summary>
    public static FlowRegistration Create<TViewModel>(
        FlowRegistrationKind kind,
        string logicalKey,
        ViewContract contract,
        FlowRegistrationFactory<TViewModel> factory,
        FlowRegistrationLocation location = default)
        where TViewModel : class =>
        CreateCore<TViewModel>(kind, logicalKey, contract, null, null, factory, location);

    /// <summary>Creates a closed content registration with a parameter or request type.</summary>
    public static FlowRegistration Create<TViewModel, TParameter>(
        FlowRegistrationKind kind,
        string logicalKey,
        ViewContract contract,
        FlowRegistrationFactory<TViewModel> factory,
        FlowRegistrationLocation location = default)
        where TViewModel : class =>
        CreateCore<TViewModel>(
            kind,
            logicalKey,
            contract,
            typeof(TParameter),
            null,
            factory,
            location);

    /// <summary>Creates a closed content registration with parameter/request and result types.</summary>
    public static FlowRegistration Create<TViewModel, TParameter, TResult>(
        FlowRegistrationKind kind,
        string logicalKey,
        ViewContract contract,
        FlowRegistrationFactory<TViewModel> factory,
        FlowRegistrationLocation location = default)
        where TViewModel : class =>
        CreateCore<TViewModel>(
            kind,
            logicalKey,
            contract,
            typeof(TParameter),
            typeof(TResult),
            factory,
            location);

    /// <summary>Invokes the explicit closed factory.</summary>
    /// <exception cref="InvalidOperationException">The registration is metadata-only.</exception>
    public ValueTask<object> CreateViewModelAsync(
        FlowActivationScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (_factory is null)
        {
            throw new InvalidOperationException(
                $"Flow registration '{Identity}' does not activate content.");
        }

        return _factory(scope, cancellationToken);
    }

    private static FlowRegistration CreateCore<TViewModel>(
        FlowRegistrationKind kind,
        string logicalKey,
        ViewContract contract,
        Type? parameterType,
        Type? resultType,
        FlowRegistrationFactory<TViewModel> factory,
        FlowRegistrationLocation location)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (string.IsNullOrEmpty(contract.Value))
        {
            throw new ArgumentException("A content registration requires a View contract.", nameof(contract));
        }

        return new FlowRegistration(
            new FlowRegistrationIdentity(kind, logicalKey),
            contract,
            typeof(TViewModel),
            parameterType,
            resultType,
            InvokeFactoryAsync,
            location);

        async ValueTask<object> InvokeFactoryAsync(
            FlowActivationScope scope,
            CancellationToken cancellationToken)
        {
            TViewModel? viewModel = await factory(scope, cancellationToken).ConfigureAwait(false);
            return viewModel ?? throw new FlowRegistrationException(
                $"The closed factory for Flow registration '{kind}:{logicalKey}' returned null.",
                logicalKey);
        }
    }
}
