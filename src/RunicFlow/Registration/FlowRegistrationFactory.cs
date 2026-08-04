using System;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow;

/// <summary>
/// Supplies the session-local service provider to an explicit Flow factory.
/// </summary>
/// <remarks>
/// The core package does not create dependency-injection scopes. A host adapter creates a
/// scope and passes its provider through this BCL-only boundary.
/// </remarks>
public sealed class FlowActivationScope
{
    /// <summary>Initializes a factory scope around a session-local service provider.</summary>
    public FlowActivationScope(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>Gets the session-local service provider.</summary>
    public IServiceProvider Services { get; }
}

/// <summary>Creates a closed ViewModel without reflection or assembly scanning.</summary>
/// <typeparam name="TViewModel">The declared ViewModel type.</typeparam>
/// <param name="scope">The session-local activation scope.</param>
/// <param name="cancellationToken">Cancels activation before presentation commits.</param>
/// <returns>The newly activated ViewModel.</returns>
public delegate ValueTask<TViewModel> FlowRegistrationFactory<TViewModel>(
    FlowActivationScope scope,
    CancellationToken cancellationToken)
    where TViewModel : class;
