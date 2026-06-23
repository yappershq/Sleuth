using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using ConnectionMessages.Shared;
using HexTags.Shared;
using Microsoft.Extensions.Logging;
using Sharp.Extensions.GameEventManager;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEvents;
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
    private readonly IGameEventManager     _eventManager;

    // Cross-plugin refs — resolved in OAM, null-safe if plugins absent
    private IHexTagsShared?            _hexTags;
    private IConnectionMessagesShared? _connMsg;

    int IClientListener.ListenerVersion  => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;

    public SleuthModule(
        InterfaceBridge       bridge,
        ILogger<SleuthModule> logger,
        ISleuthConfig         config,
        IGameEventManager     eventManager)
    {
        _bridge       = bridge;
        _logger       = logger;
        _config       = config;
        _eventManager = eventManager;

        for (var i = 0; i < 64; i++)
            _savedTeam[i] = CStrikeTeam.UnAssigned;
    }

    // ===== IModule =====

    public bool Init()
    {
        _bridge.ClientManager.InstallClientListener(this);
        return true;
    }

    public void OnPostInit()
    {
        // Hook "player_team" once for the plugin's lifetime. The handler is a no-op
        // (returns Ignored) for every slot that is NOT currently in covert mode, so normal
        // team changes for everyone else broadcast exactly as before. We only suppress the
        // broadcast for slots flagged _active[] — i.e. the covert admin whose team we move.
        // This is the canonical blockable game-event hook (see TTT PlayerModule.OnPlayerTeam).
        _eventManager.HookEvent("player_team", OnPlayerTeam);
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

        // (c) Save current team and switch to configured stealth team.
        //
        // SCOREBOARD HIDE (feasible, implemented here):
        //   Moving the controller to UnAssigned (0) drops it off ALL THREE rendered scoreboard
        //   sections (CT / T / Spectators). Spectator (1) only hides from the playing teams —
        //   the player still shows under the "Spectators" header — so UnAssigned hides better.
        //   The team-change broadcast that would otherwise announce this move is suppressed by
        //   OnPlayerTeam above (covert slots only).
        //
        // 'status' CONSOLE LIST (NOT feasible — see README/limitation note):
        //   The native 'status' command output cannot be filtered per-row. ModSharp only exposes
        //   IHookManager.PrintStatus, which fires once per invocation and supports just
        //   Ignored / SkipCallReturnOverride keyed on the *requester* — it's all-or-nothing for
        //   the whole table, with no per-row buffer to edit. Surgically removing only the covert
        //   admin's row while leaving the rest of the table intact is impossible, and suppressing
        //   the entire table for normal players would break a legitimate command for everyone.
        //   We therefore do NOT touch 'status' — the covert admin's slot still appears in the
        //   `status` console list (visible via RCON / console), and that is documented as a known
        //   limitation rather than faked.
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

    // ===== player_team suppression =====

    /// <summary>
    /// Hooked for the plugin lifetime. Suppresses the "X joined Spectators / Counter-Terrorists"
    /// broadcast ONLY for slots currently in covert mode — every other team change is left
    /// untouched (returns <see cref="EHookAction.Ignored"/>).
    /// <para>
    /// Resolution order for the slot: the typed event's <c>Controller</c> first; if that is null
    /// (it can be during teardown) fall back to resolving via <c>UserId</c> through
    /// <see cref="Sharp.Shared.Managers.IClientManager"/>.
    /// </para>
    /// <para>
    /// Suppression strategy: set the event's <c>Silent</c> flag (engine's own "no announcement"
    /// switch) AND block the client broadcast via <see cref="EHookAction.ChangeParamReturnDefault"/>
    /// with <paramref name="serverOnly"/> = true — this lets the engine still process the team
    /// change server-side (so the move actually happens / scoreboard team updates) while clients
    /// never see the join message. (SkipCallReturnOverride would short-circuit before serverOnly
    /// is applied and skip the engine's own bookkeeping, so we use ChangeParamReturnDefault here.)
    /// </para>
    /// </summary>
    private HookReturnValue<bool> OnPlayerTeam(IGameEvent gameEvent, ref bool serverOnly)
    {
        if (gameEvent is not IEventPlayerTeam evt)
            return new HookReturnValue<bool>(EHookAction.Ignored);

        // Resolve the affected slot. Controller is the cheap path; fall back to UserId.
        var slot = ResolveSlot(evt);
        if (slot < 0)
            return new HookReturnValue<bool>(EHookAction.Ignored);

        // Not a covert slot → leave the event completely alone (normal broadcast).
        if (!_active[slot])
            return new HookReturnValue<bool>(EHookAction.Ignored);

        // Covert slot → silence the announcement and suppress the client broadcast,
        // but let the engine still apply the team change server-side.
        evt.Silent = true;
        serverOnly = true;
        return new HookReturnValue<bool>(EHookAction.ChangeParamReturnDefault);
    }

    /// <summary>Resolve the 0–63 player slot for a player_team event, or -1 if unresolvable.</summary>
    private int ResolveSlot(IEventPlayerTeam evt)
    {
        if (evt.Controller is { } controller)
            return (int)(byte)controller.PlayerSlot;

        var client = _bridge.ClientManager.GetGameClient(evt.UserId);
        if (client is not null)
            return (int)(byte)client.Slot;

        return -1;
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
