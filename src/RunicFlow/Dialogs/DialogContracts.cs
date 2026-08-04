using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Dialogs;

/// <summary>Identifies why a dialog close was requested.</summary>
public enum DialogCloseReason
{
    /// <summary>A typed result was supplied.</summary>
    Complete,

    /// <summary>Cancellation was requested by dialog logic or its caller.</summary>
    Cancel,

    /// <summary>The presentation surface was dismissed.</summary>
    Dismiss,

    /// <summary>The owning interaction session is shutting down.</summary>
    Shutdown,
}

/// <summary>Supplies typed context to a dialog close guard.</summary>
/// <typeparam name="TResult">The registered dialog result type.</typeparam>
public sealed record DialogCloseGuardContext<TResult>
{
    /// <summary>Initializes a close-guard context.</summary>
    public DialogCloseGuardContext(
        DialogKey dialog,
        FlowSessionId sessionId,
        DialogCloseReason reason,
        TResult? result = default)
    {
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("A dialog session identifier cannot be empty.", nameof(sessionId));
        }

        if (string.IsNullOrEmpty(dialog.Value))
        {
            throw new ArgumentException("A dialog key cannot be empty.", nameof(dialog));
        }

        Dialog = dialog;
        SessionId = sessionId;
        Reason = reason;
        Result = result;
    }

    /// <summary>Gets the registered dialog.</summary>
    public DialogKey Dialog { get; }

    /// <summary>Gets the dialog content session.</summary>
    public FlowSessionId SessionId { get; }

    /// <summary>Gets the requested close reason.</summary>
    public DialogCloseReason Reason { get; }

    /// <summary>Gets the proposed typed result for <see cref="DialogCloseReason.Complete"/>.</summary>
    public TResult? Result { get; }
}

/// <summary>Allows a ViewModel to deny an ordinary dialog close request.</summary>
/// <typeparam name="TResult">The registered dialog result type.</typeparam>
/// <remarks>Shutdown bypasses this guard. A denial releases the completion claim so a later request can retry.</remarks>
public interface IDialogCloseGuard<TResult>
{
    /// <summary>Determines whether the proposed close may proceed.</summary>
    ValueTask<bool> CanCloseAsync(
        DialogCloseGuardContext<TResult> context,
        CancellationToken cancellationToken);
}

/// <summary>Completes one typed dialog conversation.</summary>
/// <typeparam name="TResult">The registered dialog result type.</typeparam>
public interface IDialogController<TResult>
{
    /// <summary>Gets whether a completion request is being evaluated or has been accepted.</summary>
    bool IsCompletionRequested { get; }

    /// <summary>Requests successful completion with a typed result.</summary>
    ValueTask<bool> CompleteAsync(
        TResult result,
        CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation.</summary>
    ValueTask<bool> CancelAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests dismissal by the presentation surface.</summary>
    ValueTask<bool> DismissAsync(CancellationToken cancellationToken = default);
}

/// <summary>Closes child conversations owned by a dialog before the dialog itself is deactivated.</summary>
public interface IDialogChildOwner
{
    /// <summary>Closes children in reverse ownership order and waits for their cleanup.</summary>
    ValueTask CloseChildrenAsync(CancellationToken cancellationToken);
}

/// <summary>Creates scoped content for one closed dialog registration.</summary>
/// <typeparam name="TViewModel">The registered ViewModel type.</typeparam>
/// <typeparam name="TRequest">The registered request type.</typeparam>
/// <typeparam name="TResult">The registered result type.</typeparam>
public delegate ValueTask<DialogContent<TViewModel>> DialogContentFactory<TViewModel, TRequest, TResult>(
    TRequest request,
    IDialogController<TResult> controller,
    CancellationToken cancellationToken)
    where TViewModel : class;

/// <summary>Transfers the resource ownership for one newly created dialog ViewModel to the runtime.</summary>
/// <typeparam name="TViewModel">The registered ViewModel type.</typeparam>
public sealed class DialogContent<TViewModel>
    where TViewModel : class
{
    /// <summary>Initializes owned dialog content.</summary>
    public DialogContent(
        TViewModel viewModel,
        object ownedScope,
        bool ownsViewModel = false,
        IReadOnlyDictionary<string, string>? metadata = null,
        IDialogChildOwner? children = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(ownedScope);
        if (ownedScope is not IAsyncDisposable && ownedScope is not IDisposable)
        {
            throw new ArgumentException(
                "A dialog content scope must implement IAsyncDisposable or IDisposable.",
                nameof(ownedScope));
        }

        ViewModel = viewModel;
        OwnedScope = ownedScope;
        OwnsViewModel = ownsViewModel;
        Metadata = metadata;
        Children = children;
    }

    /// <summary>Gets the ViewModel instance.</summary>
    public TViewModel ViewModel { get; }

    /// <summary>Gets the scope disposed after all other dialog resources.</summary>
    public object OwnedScope { get; }

    /// <summary>Gets whether the runtime disposes the ViewModel separately from the scope.</summary>
    public bool OwnsViewModel { get; }

    /// <summary>Gets bounded presentation metadata copied into the content descriptor.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    /// <summary>Gets the optional nested-dialog ownership seam.</summary>
    public IDialogChildOwner? Children { get; }
}

/// <summary>Shows registered typed dialogs for one interaction session.</summary>
public interface IDialogService
{
    /// <summary>Shows the registration identified by its closed generic types.</summary>
    ValueTask<DialogOutcome<TResult>> ShowAsync<TViewModel, TRequest, TResult>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TViewModel : class;

    /// <summary>Shows a closed registration and verifies its logical key.</summary>
    ValueTask<DialogOutcome<TResult>> ShowAsync<TViewModel, TRequest, TResult>(
        DialogKey dialog,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TViewModel : class;
}

/// <summary>Stops a dialog service and closes active dialogs in reverse open order.</summary>
public interface IDialogShutdown
{
    /// <summary>Rejects new dialogs and requests unguarded cancellation of active dialogs.</summary>
    ValueTask ShutdownAsync(CancellationToken cancellationToken = default);
}
