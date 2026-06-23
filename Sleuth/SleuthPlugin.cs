using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sharp.Shared;

namespace Sleuth;

/// <summary>
/// Sleuth — stealth-admin toggle for ModSharp.
/// Hides HexTag, silences join/leave announcements, and moves admin to an unassigned team
/// while in stealth mode. Toggle with !sleuth (requires sleuth:stealth permission).
/// </summary>
public sealed class SleuthPlugin : IModSharpModule
{
    public string DisplayName   => "Sleuth";
    public string DisplayAuthor => "yappershq";

    private readonly ILogger<SleuthPlugin> _logger;
    private readonly ServiceProvider       _serviceProvider;

    public SleuthPlugin(
        ISharedSystem   sharedSystem,
        string?         dllPath,
        string?         sharpPath,
        Version?        version,
        IConfiguration? coreConfiguration,
        bool            hotReload)
    {
        ArgumentNullException.ThrowIfNull(sharedSystem);
        ArgumentNullException.ThrowIfNull(dllPath);
        ArgumentNullException.ThrowIfNull(sharpPath);

        var loggerFactory = sharedSystem.GetLoggerFactory();
        _logger = loggerFactory.CreateLogger<SleuthPlugin>();

        _ = new InterfaceBridge(dllPath, sharpPath, sharedSystem, loggerFactory);

        var services = new ServiceCollection();
        services.AddSingleton(sharedSystem);
        services.AddSingleton(InterfaceBridge.Instance);
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(LoggerFactoryLogger<>));
        services.AddModules();

        _serviceProvider = services.BuildServiceProvider();
    }

    public bool Init()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.Init(), "Init");

        return true;
    }

    public void PostInit()
    {
        // Sleuth publishes no cross-plugin interface — nothing to do here except dispatch.
        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.OnPostInit(), "PostInit");
    }

    public void OnAllModulesLoaded()
    {
        InterfaceBridge.Instance.InitLocalizer();
        InterfaceBridge.Instance.InitAdminManager();

        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.OnAllSharpModulesLoaded(), "OnAllModulesLoaded");

        _logger.LogInformation("[Sleuth] Plugin loaded successfully");
    }

    public void Shutdown()
    {
        foreach (var module in _serviceProvider.GetServices<IModule>())
            CallSafe(module, static m => m.Shutdown(), "Shutdown");

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    public void OnLibraryConnected(string name)  { }
    public void OnLibraryDisconnect(string name) { }

    private void CallSafe(IModule module, Action<IModule> action, string phase)
    {
        try
        {
            action(module);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Sleuth] Error in {Phase} for {Module}", phase, module.GetType().Name);
        }
    }
}

internal sealed class LoggerFactoryLogger<T>(ILoggerFactory factory) : ILogger<T>
{
    private readonly ILogger _inner = factory.CreateLogger(typeof(T).Name);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _inner.Log(logLevel, eventId, state, exception, formatter);
}
