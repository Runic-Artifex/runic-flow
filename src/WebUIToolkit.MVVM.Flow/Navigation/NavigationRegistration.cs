using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Navigation;

/// <summary>Contains resources created by one closed route factory.</summary>
public sealed record NavigationRouteContent
{
    /// <summary>Initializes factory content.</summary>
    public NavigationRouteContent(
        object viewModel,
        object ownedScope,
        bool ownsViewModel = false,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(ownedScope);
        ViewModel = viewModel;
        OwnedScope = ownedScope;
        OwnsViewModel = ownsViewModel;
        Metadata = metadata;
    }

    /// <summary>Gets the factory-created ViewModel.</summary>
    public object ViewModel { get; }
    /// <summary>Gets the disposable scope owned by the content session.</summary>
    public object OwnedScope { get; }
    /// <summary>Gets whether the ViewModel is disposed independently before its scope.</summary>
    public bool OwnsViewModel { get; }
    /// <summary>Gets bounded presentation metadata copied by the content descriptor.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }
}

/// <summary>Registers one root logical navigation region.</summary>
public sealed record NavigationRegionRegistration
{
    /// <summary>Initializes a region registration.</summary>
    public NavigationRegionRegistration(
        RegionKey key,
        RouteKey? startRoute = null,
        bool requireContent = false,
        NavigationConcurrency concurrency = NavigationConcurrency.Queue)
    {
        if (string.IsNullOrEmpty(key.Value))
        {
            throw new ArgumentException("A navigation region key cannot be empty.", nameof(key));
        }

        if (startRoute is RouteKey start && string.IsNullOrEmpty(start.Value))
        {
            throw new ArgumentException("A navigation start route cannot be empty.", nameof(startRoute));
        }

        if (concurrency is not NavigationConcurrency.Queue and not NavigationConcurrency.RejectWhileBusy)
        {
            throw new ArgumentOutOfRangeException(nameof(concurrency));
        }

        Key = key;
        StartRoute = startRoute;
        RequireContent = requireContent;
        Concurrency = concurrency;
    }

    /// <summary>Gets the region key.</summary>
    public RegionKey Key { get; }
    /// <summary>Gets the optional route presented by StartAsync.</summary>
    public RouteKey? StartRoute { get; }
    /// <summary>Gets whether Clear and an empty started state are forbidden.</summary>
    public bool RequireContent { get; }
    /// <summary>Gets the region's concurrency policy.</summary>
    public NavigationConcurrency Concurrency { get; }
}

/// <summary>Builds explicit, closed, reflection-free navigation registrations.</summary>
public sealed class NavigationRegistryBuilder
{
    private readonly Dictionary<RegionKey, NavigationRegionRegistration> _regions = [];
    private readonly Dictionary<RouteKey, NavigationRouteRegistration> _routes = [];
    private readonly Dictionary<RouteSignature, RouteKey> _signatures = [];
    private bool _frozen;

    /// <summary>Adds one root region.</summary>
    public NavigationRegistryBuilder AddRegion(NavigationRegionRegistration region)
    {
        ArgumentNullException.ThrowIfNull(region);
        ThrowIfFrozen();
        if (!_regions.TryAdd(region.Key, region))
        {
            throw new FlowRegistrationException(
                $"Navigation region '{region.Key}' is already registered.",
                region.Key.Value);
        }

        return this;
    }

    /// <summary>Adds a parameterless closed route factory.</summary>
    public NavigationRegistryBuilder AddPage<TViewModel>(
        RouteKey route,
        ViewContract contract,
        Func<CancellationToken, ValueTask<NavigationRouteContent>> factory,
        NavigationRetention retention = NavigationRetention.RetainInBackStack)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddRoute(new NavigationRouteRegistration(
            route,
            contract,
            typeof(TViewModel),
            ParameterType: null,
            retention,
            async (parameter, cancellationToken) =>
            {
                if (parameter is not null)
                {
                    throw new ArgumentException($"Route '{route}' does not accept a parameter.", nameof(parameter));
                }

                NavigationRouteContent content = await factory(cancellationToken).ConfigureAwait(false);
                ValidateViewModel<TViewModel>(content, route);
                return content;
            },
            static (_, _, _) => ValueTask.CompletedTask));
    }

    /// <summary>Adds a typed, closed route factory.</summary>
    public NavigationRegistryBuilder AddPage<TViewModel, TParameter>(
        RouteKey route,
        ViewContract contract,
        Func<TParameter, CancellationToken, ValueTask<NavigationRouteContent>> factory,
        NavigationRetention retention = NavigationRetention.RetainInBackStack)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddRoute(new NavigationRouteRegistration(
            route,
            contract,
            typeof(TViewModel),
            typeof(TParameter),
            retention,
            async (parameter, cancellationToken) =>
            {
                if (!IsCompatibleParameter(typeof(TParameter), parameter))
                {
                    throw new ArgumentException(
                        $"Route '{route}' requires a parameter assignable to '{typeof(TParameter)}'.",
                        nameof(parameter));
                }

                NavigationRouteContent content = await factory((TParameter)parameter!, cancellationToken)
                    .ConfigureAwait(false);
                ValidateViewModel<TViewModel>(content, route);
                return content;
            },
            static (viewModel, parameter, cancellationToken) =>
                viewModel is IFlowInitializable<TParameter> initializable
                    ? initializable.InitializeAsync((TParameter)parameter!, cancellationToken)
                    : ValueTask.CompletedTask));
    }

    /// <summary>Freezes and validates the registry. The builder cannot be reused afterward.</summary>
    public NavigationRegistry Build()
    {
        ThrowIfFrozen();
        _frozen = true;

        foreach (NavigationRegionRegistration region in _regions.Values)
        {
            if (region.RequireContent && region.StartRoute is null)
            {
                throw new FlowValidationException(
                    $"Required-content region '{region.Key}' must declare a start route.",
                    region.Key.Value);
            }

            if (region.StartRoute is RouteKey startRoute && !_routes.ContainsKey(startRoute))
            {
                throw new FlowValidationException(
                    $"Start route '{startRoute}' for region '{region.Key}' is not registered.",
                    startRoute.Value);
            }

            if (region.StartRoute is RouteKey typedStart &&
                _routes.TryGetValue(typedStart, out NavigationRouteRegistration? startRegistration) &&
                startRegistration.ParameterType is not null)
            {
                throw new FlowValidationException(
                    $"Start route '{typedStart}' for region '{region.Key}' requires a parameter.",
                    typedStart.Value);
            }
        }

        return new NavigationRegistry(_regions, _routes, _signatures);
    }

    private NavigationRegistryBuilder AddRoute(NavigationRouteRegistration registration)
    {
        ThrowIfFrozen();
        if (string.IsNullOrEmpty(registration.Route.Value))
        {
            throw new FlowRegistrationException("A navigation route key cannot be empty.");
        }

        if (string.IsNullOrEmpty(registration.Contract.Value))
        {
            throw new FlowRegistrationException(
                $"Navigation route '{registration.Route}' has an empty presentation contract.",
                registration.Route.Value);
        }

        if (registration.Retention is not NavigationRetention.RetainInBackStack and not NavigationRetention.RecreateOnBack)
        {
            throw new ArgumentOutOfRangeException(nameof(registration));
        }

        if (!_routes.TryAdd(registration.Route, registration))
        {
            throw new FlowRegistrationException(
                $"Navigation route '{registration.Route}' is already registered.",
                registration.Route.Value);
        }

        RouteSignature signature = new(registration.ViewModelType, registration.ParameterType);
        if (!_signatures.TryAdd(signature, registration.Route))
        {
            _routes.Remove(registration.Route);
            throw new FlowRegistrationException(
                $"ViewModel '{registration.ViewModelType}' already has a route for the same parameter type.",
                registration.Route.Value);
        }

        return this;
    }

    private static void ValidateViewModel<TViewModel>(NavigationRouteContent content, RouteKey route)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.ViewModel is not TViewModel)
        {
            throw new InvalidOperationException(
                $"The factory for route '{route}' returned a ViewModel that is not assignable to '{typeof(TViewModel)}'.");
        }
    }

    private static bool IsCompatibleParameter(Type parameterType, object? parameter) =>
        parameter is not null
            ? parameterType.IsInstanceOfType(parameter)
            : !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null;

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("The navigation registry builder is frozen.");
        }
    }
}

/// <summary>Contains validated immutable navigation registrations.</summary>
public sealed class NavigationRegistry
{
    private readonly ReadOnlyDictionary<RegionKey, NavigationRegionRegistration> _regions;
    private readonly ReadOnlyDictionary<RouteKey, NavigationRouteRegistration> _routes;
    private readonly ReadOnlyDictionary<RouteSignature, RouteKey> _signatures;
    private readonly ReadOnlyCollection<NavigationRouteDescriptor> _routeDescriptors;

    internal NavigationRegistry(
        IDictionary<RegionKey, NavigationRegionRegistration> regions,
        IDictionary<RouteKey, NavigationRouteRegistration> routes,
        IDictionary<RouteSignature, RouteKey> signatures)
    {
        _regions = new ReadOnlyDictionary<RegionKey, NavigationRegionRegistration>(
            new Dictionary<RegionKey, NavigationRegionRegistration>(regions));
        _routes = new ReadOnlyDictionary<RouteKey, NavigationRouteRegistration>(
            new Dictionary<RouteKey, NavigationRouteRegistration>(routes));
        _signatures = new ReadOnlyDictionary<RouteSignature, RouteKey>(
            new Dictionary<RouteSignature, RouteKey>(signatures));
        NavigationRouteDescriptor[] descriptors = new NavigationRouteDescriptor[routes.Count];
        int index = 0;
        foreach (NavigationRouteRegistration route in routes.Values)
        {
            descriptors[index++] = new NavigationRouteDescriptor(
                route.Route,
                route.Contract,
                route.ViewModelType,
                route.ParameterType,
                route.Retention);
        }

        _routeDescriptors = new ReadOnlyCollection<NavigationRouteDescriptor>(descriptors);
    }

    /// <summary>Gets the root regions in registration order.</summary>
    public IReadOnlyCollection<NavigationRegionRegistration> Regions => _regions.Values;

    /// <summary>Gets frozen route descriptors in registration order.</summary>
    public IReadOnlyList<NavigationRouteDescriptor> Routes => _routeDescriptors;

    /// <summary>Gets whether a route is registered.</summary>
    public bool ContainsRoute(RouteKey route) => _routes.ContainsKey(route);

    internal NavigationRegionRegistration GetRegion(RegionKey region) =>
        _regions.TryGetValue(region, out NavigationRegionRegistration? registration)
            ? registration
            : throw new KeyNotFoundException($"Navigation region '{region}' is not registered.");

    internal NavigationRouteRegistration GetRoute(RouteKey route) =>
        _routes.TryGetValue(route, out NavigationRouteRegistration? registration)
            ? registration
            : throw new KeyNotFoundException($"Navigation route '{route}' is not registered.");

    internal NavigationRouteRegistration GetRoute(Type viewModelType, Type? parameterType)
    {
        RouteSignature signature = new(viewModelType, parameterType);
        if (!_signatures.TryGetValue(signature, out RouteKey route))
        {
            throw new KeyNotFoundException(
                $"No navigation route is registered for ViewModel '{viewModelType}' and the requested parameter type.");
        }

        return _routes[route];
    }
}

internal sealed record NavigationRouteRegistration(
    RouteKey Route,
    ViewContract Contract,
    Type ViewModelType,
    Type? ParameterType,
    NavigationRetention Retention,
    Func<object?, CancellationToken, ValueTask<NavigationRouteContent>> Factory,
    Func<object, object?, CancellationToken, ValueTask> Initializer)
{
    internal bool Accepts(object? parameter) => ParameterType is null
        ? parameter is null
        : parameter is not null
            ? ParameterType.IsInstanceOfType(parameter)
            : !ParameterType.IsValueType || Nullable.GetUnderlyingType(ParameterType) is not null;
}

internal readonly record struct RouteSignature(Type ViewModelType, Type? ParameterType);
