using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using ConnectionMessages.Shared;
using HexTags.Shared;
using Microsoft.Extensions.Logging;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sleuth.Configuration;

namespace Sleuth.Modules;

internal sealed class SleuthModule : IModule, IClientListener
{
    // Per-slot state (indexed by PlayerSlot byte 0–63)
    private readonly bool[]        _active    = new bool[64];           // currently in sleuth mode
    private readonly CStrikeTeam[] _savedTeam = new CStrikeTeam[64];   // team before sleuth

    private readonly InterfaceBridge      _bridge;
    private readonly ILogger<SleuthModule> _logger;
    private readonly ISleuthConfig         _config;

    // Cross-plugin refs — resolved in OAM, null-safe if plugins absent
    private IHexTagsShared?            _hexTags;
    private IConnectionMessagesShared? _connMsg;

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    public SleuthModule(InterfaceBridge bridge, ILogger<SleuthModule> logger, ISleuthConfig config)
    {
        _bridge = bridge;
        _logger = logger;
        _config = config;

        for (var i = 0; i < 64; i++)
            _savedTeam[i] = CStrikeTeam.UnAssigned;
    }

    // ===== IModule =====

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);
        return true;
    }

    public void OnAllSharpModulesLoaded()
    {
        // Resolve IHexTagsShared (optional dep)
        var hexTagsIface = _bridge.SharpModuleManager
            .GetOptionalSharpModuleInterface<IHexTagsShared>(IHexTagsShared.Identity);
        if (hexTagsIface?.Instance is { } ht)
        {
            _hexTags = ht;
            _logger.LogInformation("[Sleuth] HexTags integration enabled.");
        }
        else
        {
            _logger.LogWarning("[Sleuth] HexTags not available — tag hiding disabled.");
        }

        // Resolve IConnectionMessagesShared (optional dep)
        var connMsgIface = _bridge.SharpModuleManager
            .GetOptionalSharpModuleInterface<IConnectionMessagesShared>(IConnectionMessagesShared.Identity);
        if (connMsgIface?.Instance is { } cm)
        {
            _connMsg = cm;
            _logger.LogInformation("[Sleuth] ConnectionMessages integration enabled.");
        }
        else
        {
            _logger.LogWarning("[Sleuth] ConnectionMessages not available — announcement suppression disabled.");
        }

        // Register admin command via AdminManager
        var adminManager = _bridge.AdminManager;
        if (adminManager is null)
        {
            _logger.LogError("[Sleuth] AdminManager not available — 'sleuth' command will NOT be registered.");
            return;
        }

        adminManager.MountAdminManifest(
            "Sleuth",
            () => new AdminTableManifest(
                new Dictionary<string, HashSet<string>>
                {
                    ["sleuth:stealth"] = ["sleuth:stealth"],
                },
                [],
                []
            )
        );

        var registry = adminManager.GetCommandRegistry("Sleuth");
        registry.RegisterPermissions(ImmutableArray.Create("sleuth:stealth"));
        registry.RegisterAdminCommand(
            "sleuth",
            OnSleuthCommand,
            ImmutableArray.Create("sleuth:stealth")
        );

        _logger.LogInformation("[Sleuth] Admin command 'sleuth' registered with permission 'sleuth:stealth'.");
    }

    public void Shutdown()
    {
        _bridge.ClientManager.RemoveClientListener(this);

        // Disable sleuth for all active players on unload (best-effort)
        for (var i = 0; i < 64; i++)
        {
            if (!_active[i]) continue;
            var client = _bridge.ClientManager.GetGameClient(new Sharp.Shared.Units.PlayerSlot((byte)i));
            if (client is { IsInGame: true })
                DisableSleuth(client, i, reason: "plugin unload");
        }
    }

    // ===== Admin command handler =====

    private void OnSleuthCommand(IGameClient? invoker, StringCommand command)
    {
        if (invoker is null)
        {
            _logger.LogWarning("[Sleuth] 'sleuth' command cannot be used from server console.");
            return;
        }

        if (!_config.Enabled)
        {
            invoker.Print(HudPrintChannel.Chat, " [Sleuth] Plugin is disabled.");
            return;
        }

        var slot = (int)(byte)invoker.Slot;

        // Parse optional arg: "sleuth on" / "sleuth off" / "sleuth" (toggle)
        var arg = command.ArgCount >= 1 ? command.GetArg(1).Trim().ToLowerInvariant() : "";

        bool enable;
        if (arg == "on")
            enable = true;
        else if (arg == "off")
            enable = false;
        else
            enable = !_active[slot];   // toggle

        if (enable)
            EnableSleuth(invoker, slot);
        else
            DisableSleuth(invoker, slot, reason: "command");
    }

    // ===== Enable / Disable =====

    private void EnableSleuth(IGameClient client, int slot)
    {
        if (_active[slot])
        {
            client.Print(HudPrintChannel.Chat, " [Sleuth] Already in stealth mode.");
            return;
        }

        _active[slot] = true;

        // (a) Hide HexTag
        _hexTags?.SetHidden(slot, true);

        // (b) Silence join/leave announcements in ConnectionMessages
        if (_connMsg is not null && _config.SilenceAnnouncements)
            _connMsg.SetSilent(slot, true);

        // (c) Save current team and switch to configured stealth team
        var controller = client.GetPlayerController();
        if (controller is not null)
        {
            _savedTeam[slot] = controller.Team;

            var targetTeam = _config.SleuthTeam switch
            {
                1 => CStrikeTeam.Spectator,
                _ => CStrikeTeam.UnAssigned,
            };

            if (controller.Team != targetTeam)
                controller.SwitchTeam(targetTeam);
        }
        else
        {
            _savedTeam[slot] = CStrikeTeam.UnAssigned;
        }

        client.Print(HudPrintChannel.Chat, " [Sleuth] Stealth mode \x04ON\x01.");
        _logger.LogInformation("[Sleuth] {Name} (slot {Slot}) entered stealth mode.", client.Name, slot);
    }

    private void DisableSleuth(IGameClient client, int slot, string reason)
    {
        if (!_active[slot])
        {
            if (reason == "command")
                client.Print(HudPrintChannel.Chat, " [Sleuth] Not in stealth mode.");
            return;
        }

        _active[slot] = false;

        // (a) Restore HexTag visibility
        _hexTags?.SetHidden(slot, false);

        // (b) Restore announcement visibility
        if (_connMsg is not null && _config.SilenceAnnouncements)
            _connMsg.SetSilent(slot, false);

        // (c) Restore team
        if (_config.RestoreTeam)
        {
            var controller = client.GetPlayerController();
            if (controller is not null)
            {
                var restore = _savedTeam[slot];
                if (restore != CStrikeTeam.UnAssigned && controller.Team != restore)
                    controller.SwitchTeam(restore);
            }
        }

        _savedTeam[slot] = CStrikeTeam.UnAssigned;

        if (reason == "command")
            client.Print(HudPrintChannel.Chat, " [Sleuth] Stealth mode \x07FF4444OFF\x01.");

        _logger.LogInformation("[Sleuth] {Name} (slot {Slot}) left stealth mode ({Reason}).", client.Name, slot, reason);
    }

    // ===== IClientListener =====

    void IClientListener.OnClientDisconnected(IGameClient client, NetworkDisconnectionReason reason)
    {
        var slot = (int)(byte)client.Slot;

        // Clean up without trying to call SwitchTeam on a disconnecting client
        _active[slot]    = false;
        _savedTeam[slot] = CStrikeTeam.UnAssigned;

        // Clean up cross-plugin state
        _hexTags?.SetHidden(slot, false);
        _connMsg?.SetSilent(slot, false);
    }

    void IClientListener.OnClientConnected(IGameClient client)     { }
    void IClientListener.OnClientPutInServer(IGameClient client)   { }
    void IClientListener.OnClientDisconnecting(IGameClient client, NetworkDisconnectionReason r) { }
    void IClientListener.OnClientPostAdminCheck(IGameClient client) { }
    bool IClientListener.OnClientPreAdminCheck(IGameClient client)  => false;
    ECommandAction IClientListener.OnClientSayCommand(IGameClient client, bool teamOnly, bool isCmd, string cmd, string msg) => ECommandAction.Skipped;
    void IClientListener.OnClientSettingChanged(IGameClient client) { }
    void IClientListener.OnAdminCacheReload() { }
}
