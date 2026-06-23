using Microsoft.Extensions.Logging;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;

namespace Sleuth.Configuration;

internal interface ISleuthConfig
{
    bool Enabled { get; }
    /// <summary>
    /// Team to assign when entering sleuth mode.
    /// 0 = UnAssigned, 1 = Spectator. Default 0 (UnAssigned).
    /// </summary>
    int SleuthTeam { get; }
    /// <summary>Restore the original team when leaving sleuth mode.</summary>
    bool RestoreTeam { get; }
    /// <summary>Suppress join/leave announcements while in sleuth mode.</summary>
    bool SilenceAnnouncements { get; }
}

internal sealed class SleuthConfig : ISleuthConfig
{
    private readonly IConVar? _cvEnabled;
    private readonly IConVar? _cvSleuthTeam;
    private readonly IConVar? _cvRestoreTeam;
    private readonly IConVar? _cvSilenceAnnouncements;

    public SleuthConfig(InterfaceBridge bridge)
    {
        var cv = bridge.ConVarManager;

        _cvEnabled              = cv.CreateConVar("sleuth_enabled",               true,  "Enable Sleuth stealth-admin plugin [0=off, 1=on]");
        _cvSleuthTeam           = cv.CreateConVar("sleuth_team",                  0,     "Team when entering sleuth mode: 0=UnAssigned, 1=Spectator");
        _cvRestoreTeam          = cv.CreateConVar("sleuth_restore_team",          true,  "Restore original team when leaving sleuth mode");
        _cvSilenceAnnouncements = cv.CreateConVar("sleuth_silence_announcements", true,  "Suppress join/leave chat announcements while in sleuth mode");

        var logger = bridge.LoggerFactory.CreateLogger("Sleuth.Config");
        IConVar?[] all = [_cvEnabled, _cvSleuthTeam, _cvRestoreTeam, _cvSilenceAnnouncements];
        ConVarConfigFile.Sync(bridge.SharpPath, "sleuth.cfg", "Sleuth", logger,
            System.Array.FindAll(all, c => c is not null)!);
    }

    public bool Enabled              => _cvEnabled?.GetBool()              ?? true;
    public int  SleuthTeam           => _cvSleuthTeam?.GetInt32()          ?? 0;
    public bool RestoreTeam          => _cvRestoreTeam?.GetBool()          ?? true;
    public bool SilenceAnnouncements => _cvSilenceAnnouncements?.GetBool() ?? true;
}
