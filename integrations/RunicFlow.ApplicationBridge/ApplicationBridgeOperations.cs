using System;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Operations;
using RunicToolkit.ApplicationBridge;

namespace RunicFlow.ApplicationBridge;

/// <summary>Binds Application Bridge operation ownership to the headless Flow operation policy engine.</summary>
public static class ApplicationBridgeOperations
{
    /// <summary>
    /// Starts a backend-owned bridge operation and runs it through Flow using the same operation identifier.
    /// The application delegate remains responsible for publishing its schema-specific progress and terminal events.
    /// </summary>
    public static BridgeOperationId StartFlowOperation(
        this IBridgeOperationFactory operations,
        IOperationRunner runner,
        OperationRequest request,
        Func<BridgeOperationId, OperationContext, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);

        return operations.Start(
            async (bridgeId, operationCancellation) =>
            {
                var flowId = new OperationId(bridgeId.Value);
                OperationRequest correlated = request with
                {
                    Id = flowId,
                    CorrelationId = request.CorrelationId ?? bridgeId.Value.ToString("D"),
                };
                await runner.RunAsync(
                    correlated,
                    (context, token) => RunAsync(operation, bridgeId, context, token),
                    operationCancellation).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private static async ValueTask<bool> RunAsync(
        Func<BridgeOperationId, OperationContext, CancellationToken, ValueTask> operation,
        BridgeOperationId bridgeId,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        await operation(bridgeId, context, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
