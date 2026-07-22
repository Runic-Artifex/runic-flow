using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Dialogs;

/// <summary>Provides a presenter with logical content and its typed completion controller.</summary>
/// <typeparam name="TResult">The registered dialog result type.</typeparam>
public sealed class DialogPresentation<TResult>
{
    internal DialogPresentation(
        DialogKey dialog,
        FlowContentDescriptor content,
        IDialogController<TResult> controller,
        IReadOnlyList<FlowAction> actions)
    {
        Dialog = dialog;
        Content = content;
        Controller = controller;
        Actions = actions;
    }

    /// <summary>Gets the registered dialog key.</summary>
    public DialogKey Dialog { get; }

    /// <summary>Gets the frontend-neutral content descriptor.</summary>
    public FlowContentDescriptor Content { get; }

    /// <summary>Gets the typed, exact-once controller.</summary>
    public IDialogController<TResult> Controller { get; }

    /// <summary>Gets the immutable logical actions.</summary>
    public IReadOnlyList<FlowAction> Actions { get; }
}

/// <summary>Presents logical dialog content without exposing frontend types to Flow.</summary>
public interface IDialogPresenter
{
    /// <summary>Opens a dialog and returns the presentation resources owned by that dialog.</summary>
    ValueTask<IFlowPresentationLease> PresentAsync<TResult>(
        DialogPresentation<TResult> presentation,
        CancellationToken cancellationToken);
}

/// <summary>Maps logical presenter keys to explicit presenter instances.</summary>
public sealed class DialogPresenterRegistry
{
    private readonly ReadOnlyDictionary<PresenterKey, IDialogPresenter> _presenters;

    /// <summary>Initializes and freezes presenter registrations.</summary>
    public DialogPresenterRegistry(IReadOnlyDictionary<PresenterKey, IDialogPresenter> presenters)
    {
        ArgumentNullException.ThrowIfNull(presenters);
        Dictionary<PresenterKey, IDialogPresenter> copy = new(presenters.Count);
        foreach (KeyValuePair<PresenterKey, IDialogPresenter> item in presenters)
        {
            if (string.IsNullOrEmpty(item.Key.Value))
            {
                throw new FlowRegistrationException("A dialog presenter key cannot be empty.");
            }

            ArgumentNullException.ThrowIfNull(item.Value);
            copy.Add(item.Key, item.Value);
        }

        _presenters = new ReadOnlyDictionary<PresenterKey, IDialogPresenter>(copy);
    }

    internal IDialogPresenter Get(PresenterKey key)
    {
        if (!_presenters.TryGetValue(key, out IDialogPresenter? presenter))
        {
            throw new FlowValidationException(
                $"No dialog presenter is registered for key '{key}'.",
                key.Value);
        }

        return presenter;
    }
}
