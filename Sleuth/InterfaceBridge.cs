using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;

namespace Sleuth;

internal sealed class InterfaceBridge
{
    internal static InterfaceBridge Instance { get; private set; } = null!;

    // === Paths ===
    internal string SharpPath { get; }
    internal string DllPath   { get; }

    // === Managers ===
    internal IConVarManager      ConVarManager      { get; }
    internal IEventManager       EventManager       { get; }
    internal IClientManager      ClientManager      { get; }
    internal IHookManager        HookManager        { get; }
    internal IModSharp           ModSharp           { get; }
    internal ILoggerFactory      LoggerFactory      { get; }
    internal ISharpModuleManager SharpModuleManager { get; }

    // === Optional modules (resolved in OnAllModulesLoaded) ===
    internal ILocalizerManager? LocalizerManager { get; private set; }
    internal IAdminManager?     AdminManager     { get; private set; }

    public InterfaceBridge(
        string          dllPath,
        string          sharpPath,
        ISharedSystem   sharedSystem,
        ILoggerFactory  loggerFactory)
    {
        Instance = this;

        SharpPath = sharpPath;
        DllPath   = dllPath;

        ConVarManager      = sharedSystem.GetConVarManager();
        EventManager       = sharedSystem.GetEventManager();
        ClientManager      = sharedSystem.GetClientManager();
        HookManager        = sharedSystem.GetHookManager();
        ModSharp           = sharedSystem.GetModSharp();
        LoggerFactory      = loggerFactory;
        SharpModuleManager = sharedSystem.GetSharpModuleManager();
    }

    internal void InitLocalizer()
    {
        var iface = SharpModuleManager
            .GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity);
        if (iface?.Instance is not { } lm)
            return;

        LocalizerManager = lm;
        lm.LoadLocaleFile("sleuth", suppressDuplicationWarnings: true);
    }

    internal void InitAdminManager()
    {
        var iface = SharpModuleManager
            .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity);
        if (iface?.Instance is not { } am)
            return;

        AdminManager = am;
    }
}
