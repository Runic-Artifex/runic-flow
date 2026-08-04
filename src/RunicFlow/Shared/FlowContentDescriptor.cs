using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicFlow;

/// <summary>
/// Describes logical content passed from the Flow runtime to a presenter.
/// </summary>
/// <remarks>
/// <see cref="ViewModel"/> is heterogeneous presentation data. Typed dialog and
/// workflow results use their feature outcome contracts instead.
/// </remarks>
public sealed record FlowContentDescriptor
{
    /// <summary>
    /// Initializes a content descriptor and takes an immutable snapshot of its metadata.
    /// </summary>
    /// <param name="sessionId">The owning content session.</param>
    /// <param name="contract">The logical presentation contract.</param>
    /// <param name="viewModel">The ViewModel instance to present.</param>
    /// <param name="declaredViewModelType">The closed ViewModel type declared by registration.</param>
    /// <param name="metadata">Bounded adapter metadata. Values must not contain result payloads.</param>
    public FlowContentDescriptor(
        FlowSessionId sessionId,
        ViewContract contract,
        object viewModel,
        Type declaredViewModelType,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(declaredViewModelType);

        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("A Flow session identifier cannot be empty.", nameof(sessionId));
        }

        if (!declaredViewModelType.IsInstanceOfType(viewModel))
        {
            throw new ArgumentException(
                $"The ViewModel instance is not assignable to declared type '{declaredViewModelType}'.",
                nameof(viewModel));
        }

        SessionId = sessionId;
        Contract = contract;
        ViewModel = viewModel;
        DeclaredViewModelType = declaredViewModelType;
        Metadata = FreezeMetadata(metadata);
    }

    /// <summary>
    /// Gets the owning session identifier.
    /// </summary>
    public FlowSessionId SessionId { get; }

    /// <summary>
    /// Gets the logical presentation contract.
    /// </summary>
    public ViewContract Contract { get; }

    /// <summary>
    /// Gets the ViewModel instance.
    /// </summary>
    public object ViewModel { get; }

    /// <summary>
    /// Gets the ViewModel type declared by the closed registration.
    /// </summary>
    public Type DeclaredViewModelType { get; }

    /// <summary>
    /// Gets an immutable snapshot of adapter metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static ReadOnlyDictionary<string, string> FreezeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return ReadOnlyDictionary<string, string>.Empty;
        }

        Dictionary<string, string> copy = new(metadata.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> item in metadata)
        {
            ArgumentException.ThrowIfNullOrEmpty(item.Key);
            ArgumentNullException.ThrowIfNull(item.Value);
            copy.Add(item.Key, item.Value);
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
