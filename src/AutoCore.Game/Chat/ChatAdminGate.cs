namespace AutoCore.Game.Chat;

using AutoCore.Game.Entities;
using AutoCore.Utils.Logging;

/// <summary>
/// SS-28: flat GM authorization for slash/admin chat commands.
/// Character.GMLevel is set from Account.Level at sector transfer;
/// gated commands require GMLevel &gt;= 1.
/// Player-facing exceptions stay open: /reportbug (/bug, /bugreport).
/// </summary>
public static class ChatAdminGate
{
    public const int MinimumGmLevel = 1;

    /// <summary>
    /// Commands that require GMLevel &gt;= 1. Includes mutators and most diagnostics
    /// (/maps, /listItems, etc.). Only intentionally public commands are omitted.
    /// </summary>
    static readonly HashSet<string> MutatingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // Inventory / cargo
        "/addItem", "/additem",
        "/listItems", "/listitems",
        "/setcargo", "/setCargo",
        "/clearcargo", "/clearCargo",
        "/removeMissionCargo", "/removemissioncargo",
        "/cargoinfo", "/cargoInfo",
        "/equippedItems", "/equippeditems",

        // Missions
        "/showMissions", "/showmissions",
        "/mission",
        "/clearAllMissions", "/clearallmissions",
        "/removeCurrentMission", "/removecurrentmission",
        "/removeMission", "/removemission",
        "/giveMission", "/givemission",
        "/addMission", "/addmission",
        "/completeMission", "/completemission",

        // Combat / vehicle stats
        "/setHP", "/sethp", "/hp",
        "/setMaxHP", "/setmaxhp", "/mhp",
        "/shield", "/setShield", "/setshield",
        "/mshield", "/setMaxShield", "/setmaxshield",
        "/power", "/setPower", "/setpower",
        "/mpower", "/setMaxPower", "/setmaxpower",
        "/kill",
        "/heal",
        "/damage",
        "/god", "/invuln",
        "/combattext", "/ct",

        // Skills / progression
        "/skills",
        "/resetSkills", "/resetskills",
        "/skillpoints",
        "/level", "/setlevel",
        "/xp", "/setxp", "/addxp", "/experience",
        "/getxp", "/xpinfo",
        "/mana",
        "/tech",
        "/combat",
        "/theory",
        "/perception",
        "/attrpoints", "/attributepoints",
        "/research", "/researchpoints",

        // Currency
        "/credits", "/currency", "/money", "/setcredits", "/addcredits",

        // Map / world
        "/maps",
        "/warp", "/map",
        "/loot",
        "/spawn",
        "/teleport", "/tp",
        "/tptowaypoint", "/tpToWaypoint", "/tpwaypoint",
        "/portto", "/portTo",
        "/porttome", "/portToMe",
        "/getcbid",
        "/getnearbycbids",

        // Accounts / server
        "/addplayer", "/newaccount", "/player",
        "/sectorTick", "/sectortick", "/sector.tick",
        "/clone", "/unclone",
        "/clonetrim", "/cloneTrim",
        "/clonefollowdist", "/cloneFollowDist",
        "/clonestop", "/cloneStop",
        "/clonefollow", "/cloneFollow",
        "/cloneteleport", "/cloneTeleport", "/clonetp",
        "/clonestartpath", "/cloneStartPath",
        "/clonepathspeed", "/clonePathSpeed",

        // Moderation
        "/kick",
        "/ban",
        "/unban",
        "/listplayers", "/listPlayers",
    };

    public static bool IsMutatingCommand(string commandToken)
    {
        if (string.IsNullOrWhiteSpace(commandToken))
            return false;
        return MutatingCommands.Contains(commandToken);
    }

    public static bool Authorize(Character character, string commandToken)
    {
        if (!IsMutatingCommand(commandToken))
            return true;

        // No bound character: individual handlers already no-op / print usage. The exploit
        // class is a live player with GMLevel 0 — that is what we gate.
        if (character == null)
            return true;

        var gmLevel = character.GMLevel;
        var characterId = character?.ObjectId.Coid;
        var accountId = character?.OwningConnection?.Account?.Id;

        if (gmLevel < MinimumGmLevel)
        {
            // SS-28: deny first — never mutate state for GMLevel 0.
            GameLog.Warn("AdminCommandDenied", "SEC-001",
                ("Command", commandToken),
                ("GMLevel", gmLevel),
                ("CharacterId", characterId),
                ("AccountId", accountId));
            return false;
        }

        GameLog.Audit("AdminCommandExecuted",
            ("Command", commandToken),
            ("GMLevel", gmLevel),
            ("CharacterId", characterId),
            ("AccountId", accountId));
        return true;
    }
}
