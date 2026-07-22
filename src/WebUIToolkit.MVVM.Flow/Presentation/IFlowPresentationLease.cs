using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.MVVM.Flow;

/// <summary>
/// Represents presenter resources owned by one Flow content session.
/// </summary>
/// <remarks>
/// The runtime requests logical closure before disposing the lease. Implementations
/// must make both operations safe when invoked during cancellation and teardown.
/// </remarks>
public interface IFlowPresentationLease : IAsyncDisposable
{
    /// <summary>
    /// Removes the presented content from its logical presentation surface.
    /// </summary>
    /// <param name="cancellationToken">A token that may cancel the close request.</param>
    /// <returns>An operation representing logical closure.</returns>
    ValueTask CloseAsync(CancellationToken cancellationToken);
}
