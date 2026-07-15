namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils;

/// <summary>
/// Single entry point for C2S UseObject (0x2072). Dispatches to use-item, mission dialog,
/// vendor store open, and other Open* facility reactions without hardcoding content ids.
/// </summary>
public static class ObjectUseManager
{
    /// <summary>
    /// Handle UseObject: use-item → mission dialog (if any) → store → facility → unhandled log.
    /// </summary>
    public static void Handle(TNLConnection conn, UseObjectPacket packet)
    {
        var character = conn?.CurrentCharacter;
        if (character?.Map == null || packet == null)
            return;

        var targetCoid = packet.Target?.Coid ?? -1;
        Logger.WriteLog(LogType.Debug,
            "UseObject: charCoid={0} target={1} objectiveId={2}",
            character.ObjectId.Coid,
            targetCoid,
            packet.ObjectiveId);

        if (targetCoid <= 0)
            return;

        character.MapPresence.EnsureContinent(character.Map.ContinentId);
        if (character.MapPresence.IsSuppressed(targetCoid))
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: rejected suppressed coid={0} for char={1}",
                targetCoid,
                character.ObjectId.Coid);
            return;
        }

        // 1) Mission use-item / use-object world targets.
        if (MissionUseItemProgress.TryHandleUseObject(conn, character, targetCoid, packet.ObjectiveId))
            return;

        // 2) Mission dialog when the NPC has something to offer/turn in.
        if (NpcInteractHandler.TryHandleMissionDialog(conn, character, packet))
            return;

        // 3) Map-authored TriggerEvents (kiosk spawn → trigger → OpenStore reaction chain).
        if (InteractTriggerService.TryFire(conn, character, targetCoid))
            return;

        // 4) Vendor store spatial fallback (OpenStore near player/target).
        if (VendorStoreService.TryOpen(conn, character, targetCoid))
            return;

        // 5) Other Open* facilities (BodyShop, Garage, Refinery, SkillTrainer, …).
        if (FacilityOpenService.TryOpen(conn, character, targetCoid))
            return;

        Logger.WriteLog(LogType.Debug,
            "UseObject: no handler for target={0} charCoid={1} objectiveId={2}",
            targetCoid,
            character.ObjectId.Coid,
            packet.ObjectiveId);
    }
}
