using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.MVVM.Flow;

/// <summary>
/// Supplies immutable identity to activation lifecycle callbacks.
/// </summary>
public sealed record FlowActivationContext
{
    /// <summary>Initializes activation identity.</summary>
    public FlowActivationContext(FlowSessionId sessionId, ViewContract contract)
    {
        ValidateIdentity(sessionId, contract);
        SessionId = sessionId;
        Contract = contract;
    }

    /// <summary>Gets the content session being activated.</summary>
    public FlowSessionId SessionId { get; }

    /// <summary>Gets the logical presentation contract.</summary>
    public ViewContract Contract { get; }

    private static void ValidateIdentity(FlowSessionId sessionId, ViewContract contract)
    {
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("A Flow session identifier cannot be empty.", nameof(sessionId));
        }

        if (string.IsNullOrEmpty(contract.Value))
        {
            throw new ArgumentException("A Flow presentation contract cannot be empty.", nameof(contract));
        }
    }
}

/// <summary>
/// Supplies immutable identity to deactivation lifecycle callbacks.
/// </summary>
public sealed record FlowDeactivationContext
{
    /// <summary>Initializes deactivation identity.</summary>
    public FlowDeactivationContext(FlowSessionId sessionId, ViewContract contract)
    {
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("A Flow session identifier cannot be empty.", nameof(sessionId));
        }

        if (string.IsNullOrEmpty(contract.Value))
        {
            throw new ArgumentException("A Flow presentation contract cannot be empty.", nameof(contract));
        }

        SessionId = sessionId;
        Contract = contract;
    }

    /// <summary>Gets the content session being deactivated.</summary>
    public FlowSessionId SessionId { get; }

    /// <summary>Gets the logical presentation contract.</summary>
    public ViewContract Contract { get; }
}

/// <summary>
/// Initializes a ViewModel from a strongly typed parameter before it is presented.
/// </summary>
/// <typeparam name="TParameter">The closed registration's parameter type.</typeparam>
public interface IFlowInitializable<in TParameter>
{
    /// <summary>
    /// Initializes the ViewModel before activation begins.
    /// </summary>
    /// <param name="parameter">The typed activation parameter.</param>
    /// <param name="cancellationToken">Cancels activation before its commit point.</param>
    /// <returns>The asynchronous initialization operation.</returns>
    ValueTask InitializeAsync(TParameter parameter, CancellationToken cancellationToken);
}

/// <summary>
/// Receives ordered notifications around a content session's presentation commit.
/// </summary>
/// <remarks>
/// <c>ActivatingAsync</c> runs before presentation commits and may abort activation.
/// The other callbacks are notifications around committed state and do not change
/// the runtime's authoritative state.
/// </remarks>
public interface IFlowActivation
{
    /// <summary>Runs before content presentation commits.</summary>
    ValueTask ActivatingAsync(
        FlowActivationContext context,
        CancellationToken cancellationToken);

    /// <summary>Runs after content presentation commits.</summary>
    ValueTask ActivatedAsync(
        FlowActivationContext context,
        CancellationToken cancellationToken);

    /// <summary>Runs before committed content is closed or replaced.</summary>
    ValueTask DeactivatingAsync(
        FlowDeactivationContext context,
        CancellationToken cancellationToken);

    /// <summary>Runs after committed content is closed or replaced.</summary>
    ValueTask DeactivatedAsync(
        FlowDeactivationContext context,
        CancellationToken cancellationToken);
}
