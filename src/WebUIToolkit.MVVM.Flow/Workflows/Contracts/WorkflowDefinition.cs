using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Workflows;

/// <summary>Controls whether a visited workflow step remains alive in history.</summary>
public enum WorkflowStepRetention
{
    /// <summary>Dispose the step when it stops being current and recreate it on Back.</summary>
    RecreateOnBack,

    /// <summary>Retain the activated step while it remains in visited history.</summary>
    RetainVisited,
}

/// <summary>Contains the resources created for one workflow step activation.</summary>
public sealed record WorkflowStepActivation
{
    /// <summary>Initializes an activation whose scope is always owned by the runtime.</summary>
    public WorkflowStepActivation(object viewModel, object scope, bool ownsViewModel = false)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(scope);
        if (scope is not IDisposable && scope is not IAsyncDisposable)
        {
            throw new ArgumentException(
                "A workflow step scope must implement IDisposable or IAsyncDisposable.",
                nameof(scope));
        }

        ViewModel = viewModel;
        Scope = scope;
        OwnsViewModel = ownsViewModel;
    }

    /// <summary>Gets the step ViewModel.</summary>
    public object ViewModel { get; }

    /// <summary>Gets the owned activation scope.</summary>
    public object Scope { get; }

    /// <summary>Gets whether the runtime disposes the ViewModel separately from the scope.</summary>
    public bool OwnsViewModel { get; }
}

/// <summary>Describes one typed, immutable workflow step.</summary>
public sealed record WorkflowStepDefinition<TContext>
{
    private readonly Func<TContext, bool> _includeWhen;
    private readonly Func<TContext, CancellationToken, ValueTask<WorkflowStepActivation>> _activateAsync;

    /// <summary>Initializes a step definition.</summary>
    public WorkflowStepDefinition(
        StepKey key,
        ViewContract contract,
        Type viewModelType,
        Func<TContext, CancellationToken, ValueTask<WorkflowStepActivation>> activateAsync,
        Func<TContext, bool>? includeWhen = null,
        WorkflowStepRetention retention = WorkflowStepRetention.RecreateOnBack)
    {
        if (string.IsNullOrEmpty(key.Value))
        {
            throw new ArgumentException("A workflow step key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrEmpty(contract.Value))
        {
            throw new ArgumentException("A workflow step contract cannot be empty.", nameof(contract));
        }

        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(activateAsync);
        Key = key;
        Contract = contract;
        ViewModelType = viewModelType;
        _activateAsync = activateAsync;
        _includeWhen = includeWhen ?? (static _ => true);
        Retention = retention;
    }

    /// <summary>Gets the step key.</summary>
    public StepKey Key { get; }

    /// <summary>Gets the logical presentation contract.</summary>
    public ViewContract Contract { get; }

    /// <summary>Gets the declared ViewModel type.</summary>
    public Type ViewModelType { get; }

    /// <summary>Gets the history retention policy.</summary>
    public WorkflowStepRetention Retention { get; }

    /// <summary>Evaluates whether this step participates for the supplied context.</summary>
    public bool IsIncluded(TContext context) => _includeWhen(context);

    /// <summary>Creates the ViewModel and owned scope for one activation.</summary>
    public ValueTask<WorkflowStepActivation> ActivateAsync(
        TContext context,
        CancellationToken cancellationToken) => _activateAsync(context, cancellationToken);
}

/// <summary>Describes an ordered, conditional edge in a workflow graph.</summary>
public sealed record WorkflowEdge<TContext>
{
    private readonly Func<TContext, bool> _when;

    /// <summary>Initializes an edge.</summary>
    public WorkflowEdge(StepKey from, StepKey to, Func<TContext, bool>? when = null)
    {
        if (string.IsNullOrEmpty(from.Value))
        {
            throw new ArgumentException("A workflow edge source cannot be empty.", nameof(from));
        }

        if (string.IsNullOrEmpty(to.Value))
        {
            throw new ArgumentException("A workflow edge target cannot be empty.", nameof(to));
        }

        From = from;
        To = to;
        _when = when ?? (static _ => true);
    }

    /// <summary>Gets the source step.</summary>
    public StepKey From { get; }

    /// <summary>Gets the target step.</summary>
    public StepKey To { get; }

    /// <summary>Evaluates whether this edge is available.</summary>
    public bool IsAvailable(TContext context) => _when(context);
}

/// <summary>Represents a validated immutable workflow graph and its typed result factory.</summary>
public sealed class WorkflowDefinition<TContext, TResult>
{
    private readonly Func<TContext, TResult> _resultFactory;

    internal WorkflowDefinition(
        WorkflowKey key,
        int schemaVersion,
        StepKey start,
        IReadOnlyDictionary<StepKey, WorkflowStepDefinition<TContext>> steps,
        IReadOnlyDictionary<StepKey, IReadOnlyList<WorkflowEdge<TContext>>> edges,
        Func<TContext, TResult> resultFactory)
    {
        Key = key;
        SchemaVersion = schemaVersion;
        Start = start;
        Steps = steps;
        Edges = edges;
        _resultFactory = resultFactory;
    }

    /// <summary>Gets the logical workflow key.</summary>
    public WorkflowKey Key { get; }

    /// <summary>Gets the positive consumer-owned schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the configured start step.</summary>
    public StepKey Start { get; }

    /// <summary>Gets the closed application context type.</summary>
    public Type ContextType => typeof(TContext);

    /// <summary>Gets the closed workflow result type.</summary>
    public Type ResultType => typeof(TResult);

    /// <summary>Gets the immutable step registry.</summary>
    public IReadOnlyDictionary<StepKey, WorkflowStepDefinition<TContext>> Steps { get; }

    /// <summary>Gets immutable, registration-ordered outgoing edges by source step.</summary>
    public IReadOnlyDictionary<StepKey, IReadOnlyList<WorkflowEdge<TContext>>> Edges { get; }

    internal TResult CreateResult(TContext context) => _resultFactory(context);
}

/// <summary>Builds and validates a typed immutable workflow definition.</summary>
public sealed class WorkflowDefinitionBuilder<TContext, TResult>
{
    private readonly WorkflowKey _key;
    private readonly int _schemaVersion;
    private readonly Dictionary<StepKey, WorkflowStepDefinition<TContext>> _steps = [];
    private readonly List<WorkflowEdge<TContext>> _edges = [];
    private StepKey? _start;
    private Func<TContext, TResult>? _resultFactory;

    /// <summary>Initializes a definition builder.</summary>
    public WorkflowDefinitionBuilder(WorkflowKey key, int schemaVersion)
    {
        if (string.IsNullOrEmpty(key.Value))
        {
            throw new ArgumentException("A workflow key cannot be empty.", nameof(key));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        _key = key;
        _schemaVersion = schemaVersion;
    }

    /// <summary>Adds one typed step.</summary>
    public WorkflowDefinitionBuilder<TContext, TResult> AddStep<TViewModel>(
        StepKey key,
        ViewContract contract,
        Func<TContext, CancellationToken, ValueTask<WorkflowStepActivation>> activateAsync,
        Func<TContext, bool>? includeWhen = null,
        WorkflowStepRetention retention = WorkflowStepRetention.RecreateOnBack)
    {
        var step = new WorkflowStepDefinition<TContext>(
            key, contract, typeof(TViewModel), activateAsync, includeWhen, retention);
        if (!_steps.TryAdd(key, step))
        {
            throw GraphError($"Workflow '{_key}' contains duplicate step '{key}'.", key);
        }

        return this;
    }

    /// <summary>Adds an ordered edge. The first available outgoing edge is selected.</summary>
    public WorkflowDefinitionBuilder<TContext, TResult> AddTransition(
        StepKey from,
        StepKey to,
        Func<TContext, bool>? when = null)
    {
        _edges.Add(new WorkflowEdge<TContext>(from, to, when));
        return this;
    }

    /// <summary>Sets the start step.</summary>
    public WorkflowDefinitionBuilder<TContext, TResult> StartWith(StepKey step)
    {
        _start = step;
        return this;
    }

    /// <summary>Sets the typed finish result factory.</summary>
    public WorkflowDefinitionBuilder<TContext, TResult> FinishWith(Func<TContext, TResult> resultFactory)
    {
        ArgumentNullException.ThrowIfNull(resultFactory);
        _resultFactory = resultFactory;
        return this;
    }

    /// <summary>Validates the graph and creates an immutable definition.</summary>
    public WorkflowDefinition<TContext, TResult> Build()
    {
        if (_steps.Count == 0)
        {
            throw GraphError($"Workflow '{_key}' must contain at least one step.");
        }

        StepKey start = _start ?? throw GraphError($"Workflow '{_key}' has no start step.");
        if (!_steps.ContainsKey(start))
        {
            throw GraphError($"Workflow '{_key}' start step '{start}' is not registered.", start);
        }

        Func<TContext, TResult> resultFactory = _resultFactory ??
            throw GraphError($"Workflow '{_key}' has no result factory.");

        Dictionary<StepKey, List<WorkflowEdge<TContext>>> mutableEdges = [];
        foreach (WorkflowEdge<TContext> edge in _edges)
        {
            if (!_steps.ContainsKey(edge.From))
            {
                throw GraphError($"Workflow '{_key}' edge source '{edge.From}' is not registered.", edge.From);
            }

            if (!_steps.ContainsKey(edge.To))
            {
                throw GraphError($"Workflow '{_key}' edge target '{edge.To}' is not registered.", edge.To);
            }

            if (!mutableEdges.TryGetValue(edge.From, out List<WorkflowEdge<TContext>>? outgoing))
            {
                outgoing = [];
                mutableEdges.Add(edge.From, outgoing);
            }

            outgoing.Add(edge);
        }

        ValidateReachability(start, mutableEdges);

        Dictionary<StepKey, WorkflowStepDefinition<TContext>> stepCopy = new(_steps);
        Dictionary<StepKey, IReadOnlyList<WorkflowEdge<TContext>>> edgeCopy = [];
        foreach (KeyValuePair<StepKey, List<WorkflowEdge<TContext>>> item in mutableEdges)
        {
            edgeCopy.Add(item.Key, new ReadOnlyCollection<WorkflowEdge<TContext>>([.. item.Value]));
        }

        return new WorkflowDefinition<TContext, TResult>(
            _key,
            _schemaVersion,
            start,
            new ReadOnlyDictionary<StepKey, WorkflowStepDefinition<TContext>>(stepCopy),
            new ReadOnlyDictionary<StepKey, IReadOnlyList<WorkflowEdge<TContext>>>(edgeCopy),
            resultFactory);
    }

    private void ValidateReachability(
        StepKey start,
        Dictionary<StepKey, List<WorkflowEdge<TContext>>> edges)
    {
        HashSet<StepKey> reached = [start];
        Queue<StepKey> pending = new();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            StepKey current = pending.Dequeue();
            if (!edges.TryGetValue(current, out List<WorkflowEdge<TContext>>? outgoing))
            {
                continue;
            }

            foreach (WorkflowEdge<TContext> edge in outgoing)
            {
                if (reached.Add(edge.To))
                {
                    pending.Enqueue(edge.To);
                }
            }
        }

        foreach (StepKey step in _steps.Keys)
        {
            if (!reached.Contains(step))
            {
                throw GraphError($"Workflow '{_key}' step '{step}' is unreachable from '{start}'.", step);
            }
        }
    }

    private WorkflowGraphException GraphError(string message, StepKey? step = null) =>
        new(message, _key, step);
}
