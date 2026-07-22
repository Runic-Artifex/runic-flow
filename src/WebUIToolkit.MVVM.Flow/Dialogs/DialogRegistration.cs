using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Dialogs;

/// <summary>Describes one closed, typed dialog registration.</summary>
public sealed class DialogRegistration<TViewModel, TRequest, TResult>
    where TViewModel : class
{
    /// <summary>Initializes a closed dialog registration.</summary>
    public DialogRegistration(
        DialogKey key,
        ViewContract contract,
        PresenterKey presenter,
        DialogContentFactory<TViewModel, TRequest, TResult> contentFactory,
        IReadOnlyList<FlowAction>? actions = null)
    {
        ArgumentNullException.ThrowIfNull(contentFactory);
        if (string.IsNullOrEmpty(key.Value))
        {
            throw new FlowRegistrationException("A dialog key cannot be empty.");
        }

        if (string.IsNullOrEmpty(contract.Value))
        {
            throw new FlowRegistrationException(
                "A dialog View contract cannot be empty.",
                key.Value);
        }

        if (string.IsNullOrEmpty(presenter.Value))
        {
            throw new FlowRegistrationException(
                "A dialog presenter key cannot be empty.",
                key.Value);
        }

        Key = key;
        Contract = contract;
        Presenter = presenter;
        ContentFactory = contentFactory;
        Actions = CopyAndValidateActions(actions);
    }

    /// <summary>Gets the logical dialog key.</summary>
    public DialogKey Key { get; }

    /// <summary>Gets the logical View contract.</summary>
    public ViewContract Contract { get; }

    /// <summary>Gets the presenter policy key.</summary>
    public PresenterKey Presenter { get; }

    /// <summary>Gets the closed content factory.</summary>
    public DialogContentFactory<TViewModel, TRequest, TResult> ContentFactory { get; }

    /// <summary>Gets the immutable logical action snapshot.</summary>
    public IReadOnlyList<FlowAction> Actions { get; }

    private static IReadOnlyList<FlowAction> CopyAndValidateActions(IReadOnlyList<FlowAction>? actions)
    {
        if (actions is null || actions.Count == 0)
        {
            return Array.Empty<FlowAction>();
        }

        FlowAction[] copy = new FlowAction[actions.Count];
        HashSet<ActionKey> keys = [];
        bool hasDefault = false;
        bool hasCancel = false;
        for (int index = 0; index < actions.Count; index++)
        {
            FlowAction action = actions[index] ??
                throw new ArgumentException("Dialog actions cannot contain null.", nameof(actions));
            if (string.IsNullOrEmpty(action.Key.Value))
            {
                throw new FlowRegistrationException("A dialog action key cannot be empty.");
            }

            if (!keys.Add(action.Key))
            {
                throw new FlowRegistrationException(
                    $"Dialog action '{action.Key}' is registered more than once.",
                    action.Key.Value);
            }

            if (action.IsDefault && hasDefault)
            {
                throw new FlowRegistrationException(
                    "A dialog registration can define at most one default action.",
                    action.Key.Value);
            }

            if (action.IsCancel && hasCancel)
            {
                throw new FlowRegistrationException(
                    "A dialog registration can define at most one cancel action.",
                    action.Key.Value);
            }

            hasDefault |= action.IsDefault;
            hasCancel |= action.IsCancel;
            copy[index] = action;
        }

        return new ReadOnlyCollection<FlowAction>(copy);
    }
}

/// <summary>Collects explicit dialog registrations and freezes them into an immutable registry.</summary>
public sealed class DialogRegistryBuilder
{
    private readonly Dictionary<DialogKey, IDialogRegistration> _byKey = [];
    private readonly Dictionary<DialogTypeIdentity, IDialogRegistration> _byTypes = [];
    private bool _built;

    /// <summary>Creates and adds a closed dialog registration.</summary>
    public DialogRegistryBuilder Add<TViewModel, TRequest, TResult>(
        DialogKey key,
        ViewContract contract,
        PresenterKey presenter,
        DialogContentFactory<TViewModel, TRequest, TResult> contentFactory,
        IReadOnlyList<FlowAction>? actions = null)
        where TViewModel : class =>
        Add(new DialogRegistration<TViewModel, TRequest, TResult>(
            key,
            contract,
            presenter,
            contentFactory,
            actions));

    /// <summary>Adds a closed dialog registration.</summary>
    public DialogRegistryBuilder Add<TViewModel, TRequest, TResult>(
        DialogRegistration<TViewModel, TRequest, TResult> registration)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(registration);
        ObjectDisposedException.ThrowIf(_built, this);

        DialogRegistrationAdapter<TViewModel, TRequest, TResult> adapter = new(registration);
        DialogTypeIdentity identity = DialogTypeIdentity.Create<TViewModel, TRequest, TResult>();
        if (_byKey.ContainsKey(registration.Key))
        {
            throw new FlowRegistrationException(
                $"Dialog key '{registration.Key}' is already registered.",
                registration.Key.Value);
        }

        if (_byTypes.ContainsKey(identity))
        {
            throw new FlowRegistrationException(
                "The same closed ViewModel, request, and result types are already registered.",
                registration.Key.Value);
        }

        _byKey.Add(registration.Key, adapter);
        _byTypes.Add(identity, adapter);
        return this;
    }

    /// <summary>Freezes the registrations. The builder cannot be used again.</summary>
    public DialogRegistry Build()
    {
        ObjectDisposedException.ThrowIf(_built, this);
        _built = true;
        return new DialogRegistry(_byKey, _byTypes);
    }
}

/// <summary>Provides allocation-free lookup of frozen dialog registrations.</summary>
public sealed class DialogRegistry
{
    private readonly ReadOnlyDictionary<DialogKey, IDialogRegistration> _byKey;
    private readonly ReadOnlyDictionary<DialogTypeIdentity, IDialogRegistration> _byTypes;

    internal DialogRegistry(
        IReadOnlyDictionary<DialogKey, IDialogRegistration> byKey,
        IReadOnlyDictionary<DialogTypeIdentity, IDialogRegistration> byTypes)
    {
        _byKey = new ReadOnlyDictionary<DialogKey, IDialogRegistration>(
            new Dictionary<DialogKey, IDialogRegistration>(byKey));
        _byTypes = new ReadOnlyDictionary<DialogTypeIdentity, IDialogRegistration>(
            new Dictionary<DialogTypeIdentity, IDialogRegistration>(byTypes));
    }

    /// <summary>Gets the number of frozen dialog registrations.</summary>
    public int Count => _byKey.Count;

    /// <summary>Determines whether a logical dialog key is registered.</summary>
    public bool Contains(DialogKey key) => _byKey.ContainsKey(key);

    internal DialogRegistration<TViewModel, TRequest, TResult> Get<TViewModel, TRequest, TResult>(
        DialogKey? expectedKey = null)
        where TViewModel : class
    {
        DialogTypeIdentity identity = DialogTypeIdentity.Create<TViewModel, TRequest, TResult>();
        if (!_byTypes.TryGetValue(identity, out IDialogRegistration? registration))
        {
            throw new FlowRegistrationException(
                "No dialog is registered for the requested closed ViewModel, request, and result types.",
                expectedKey?.Value);
        }

        if (expectedKey is DialogKey key && registration.Key != key)
        {
            throw new FlowRegistrationException(
                $"Dialog key '{key}' does not identify the requested closed registration.",
                key.Value);
        }

        return ((DialogRegistrationAdapter<TViewModel, TRequest, TResult>)registration).Registration;
    }
}

internal interface IDialogRegistration
{
    DialogKey Key { get; }
}

internal sealed class DialogRegistrationAdapter<TViewModel, TRequest, TResult> : IDialogRegistration
    where TViewModel : class
{
    internal DialogRegistrationAdapter(DialogRegistration<TViewModel, TRequest, TResult> registration) =>
        Registration = registration;

    public DialogKey Key => Registration.Key;

    internal DialogRegistration<TViewModel, TRequest, TResult> Registration { get; }
}

internal readonly record struct DialogTypeIdentity(Type ViewModel, Type Request, Type Result)
{
    public static DialogTypeIdentity Create<TViewModel, TRequest, TResult>() =>
        new(typeof(TViewModel), typeof(TRequest), typeof(TResult));
}
