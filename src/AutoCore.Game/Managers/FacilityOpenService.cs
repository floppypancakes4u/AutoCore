namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Utils;

/// <summary>
/// Open* facility UIs (BodyShop, Garage, Refinery, SkillTrainer, …) via map reactions + 0x206C.
/// Same pattern as <see cref="VendorStoreService"/>; data-driven by <see cref="ReactionType"/>.
/// </summary>
public static class FacilityOpenService
{
    private static readonly ReactionType[] FacilityTypes =
    [
        ReactionType.OpenBodyShop,
        ReactionType.OpenRefinery,
        ReactionType.OpenGarage,
        ReactionType.OpenSkillTrainer,
        ReactionType.OpenArena,
        ReactionType.OpenClanManager,
    ];

    /// <summary>
    /// Fire the nearest in-range facility Open* reaction for the click. Returns true if fired.
    /// </summary>
    public static bool TryOpen(TNLConnection conn, Character character, long targetCoid)
    {
        if (character?.Map == null || targetCoid <= 0)
            return false;

        var playerPos = NpcInteractHandler.GetPlayerInteractPosition(character);
        var reaction = FindBestFacilityReaction(character.Map, playerPos, targetCoid);
        if (reaction == null)
            return false;

        Logger.WriteLog(LogType.Debug,
            "UseObject: facility Open* type={0} reaction={1} target={2} charCoid={3}",
            reaction.Template.ReactionType,
            reaction.ObjectId.Coid,
            targetCoid,
            character.ObjectId.Coid);

        character.Map.TriggerReactions(
            character.CurrentVehicle ?? (ClonedObjectBase)character,
            new List<long> { reaction.ObjectId.Coid });

        return true;
    }

    internal static Reaction FindBestFacilityReaction(SectorMap map, Vector3 playerPos, long targetCoid)
    {
        if (map?.Objects == null)
            return null;

        var maxSq = VendorStoreService.MaxOpenDistance * VendorStoreService.MaxOpenDistance;
        Reaction best = null;
        var bestDist = float.MaxValue;

        foreach (var kvp in map.Objects)
        {
            if (kvp.Value is not Reaction reaction)
                continue;
            if (reaction.Template == null || !FacilityTypes.Contains(reaction.Template.ReactionType))
                continue;

            var dist = NpcInteractHandler.DistXZSq(playerPos, reaction.Position);
            // Prefer reactions whose Objects list includes the click target, or GenericVar1 matches.
            var linked = reaction.Template.GenericVar1 == targetCoid
                || reaction.ObjectId.Coid == targetCoid
                || (reaction.Template.Objects != null && reaction.Template.Objects.Contains(targetCoid));

            if (!linked && dist > maxSq)
                continue;

            if (linked)
                dist = Math.Min(dist, maxSq * 0.25f); // strong preference for explicit link

            if (dist <= maxSq && dist < bestDist)
            {
                bestDist = dist;
                best = reaction;
            }
        }

        return best;
    }
}
