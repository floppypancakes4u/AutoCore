namespace AutoCore.Game.Map;

using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public class SectorMap
{
    public int ContinentId { get; }
    public long LocalCoidCounter { get; set; }
    public MapData MapData { get; private set; }
    public ContinentObject ContinentObject => MapData.ContinentObject;
    public Dictionary<TFID, ClonedObjectBase> Objects { get; } = new();
    public Dictionary<TFID, Trigger> Triggers { get; } = new();
    public Dictionary<TFID, Reaction> Reactions { get; } = new();

    public SectorMap(int continentId)
    {
        ContinentId = continentId;

        MapData = AssetManager.Instance.GetMapData(ContinentId);
        LocalCoidCounter = MapData.HighestCoid + 1;

        InitializeLocalObjects();
    }

    /// <summary>
    /// Builds a minimal in-memory map for unit tests (no GLM/WAD I/O).
    /// </summary>
    internal static SectorMap CreateForTests(ContinentObject continentObject, Vector4 entryPoint)
    {
        ArgumentNullException.ThrowIfNull(continentObject);

        var map = (SectorMap)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SectorMap));
        var mapData = new MapData(continentObject);
        typeof(MapData).GetProperty(nameof(MapData.EntryPoint))!
            .SetValue(mapData, entryPoint);

        typeof(SectorMap).GetField($"<{nameof(ContinentId)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, continentObject.Id);
        typeof(SectorMap).GetField($"<{nameof(MapData)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, mapData);
        typeof(SectorMap).GetField($"<{nameof(Objects)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new Dictionary<TFID, ClonedObjectBase>());
        typeof(SectorMap).GetField($"<{nameof(Triggers)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new Dictionary<TFID, Trigger>());
        typeof(SectorMap).GetField($"<{nameof(Reactions)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new Dictionary<TFID, Reaction>());

        map.LocalCoidCounter = mapData.HighestCoid + 1;
        return map;
    }

    private void InitializeLocalObjects()
    {
        // TODO: some objects are only needed on Sector? Global? Both?

        foreach (var template in MapData.Templates)
        {
            var obj = template.Value.Create();
            if (obj == null)
                continue;

            obj.LoadCloneBase(template.Value.CBID);
            obj.SetCoid(template.Value.COID, false);
            obj.Faction = template.Value.Faction;
            obj.Layer = template.Value.Layer;
            obj.SetMap(this);

            // Do NOT CreateGhost for all GraphicsObjects here — flooding the ghost table
            // with every map prop exhausts client ghost slots and NPCs stop appearing.
            // Combat props get ghosts lazily via MakeNotInvincible / first TakeDamage.

            if (obj is SpawnPoint sp)
            {
                sp.Spawn();
            }
        }
    }

    public ClonedObjectBase GetLocalObject(long coid)
    {
        if (Objects.TryGetValue(new TFID(coid, false), out var value))
            return value;

        return null;
    }

    public ClonedObjectBase GetObject(long coid)
    {
        // Try local first, then global
        if (Objects.TryGetValue(new TFID(coid, false), out var localValue))
            return localValue;

        if (Objects.TryGetValue(new TFID(coid, true), out var globalValue))
            return globalValue;

        return null;
    }

    public ClonedObjectBase GetObjectByCoid(long coid)
    {
        // Search by COID only, ignoring the Global flag
        foreach (var kvp in Objects)
        {
            if (kvp.Key.Coid == coid)
                return kvp.Value;
        }

        return null;
    }

    public void Fill(MapInfoPacket packet)
    {
        packet.RegionId = -1;
        packet.RegionType = TilesetType.Universal;
        packet.RegionLevel = 1;
        packet.LayerId = 0; // TODO: CVOGCharacter::ChooseLayer
        packet.ObjectiveIndex = ContinentObject.Objective;
        packet.MapName = $"{ContinentObject.MapFileName}.fam";
        packet.IsTown = ContinentObject.IsTown;
        packet.IsArena = ContinentObject.IsArena;
        packet.OwningFaction = ContinentObject.OwningFaction;
        packet.ContinentObjectId = ContinentId;
        packet.IsPersistent = ContinentObject.IsPersistent;
        packet.MapIterationVersion = MapData.IterationVersion;
        packet.ContestedMissionId = ContinentObject.ContestedMission;
        packet.Coid = ContinentId;
        packet.TemporalRandomSeed = 123456789;
        packet.CoidMap = ContinentId;
        packet.NumModulePlacements = 0;
        packet.PositionX = 0.0f;
        packet.PositionY = 0.0f;
        packet.PositionZ = 0.0f;
        packet.WeatherUpdateSize = 0;
    }

    public void EnterMap(ClonedObjectBase clonedObject)
    {
        if (clonedObject is Trigger trigger)
            Triggers.Add(trigger.ObjectId, trigger);

        if (clonedObject is Reaction reaction)
            Reactions.Add(reaction.ObjectId, reaction);

        if (Objects.ContainsKey(clonedObject.ObjectId))
            throw new InvalidOperationException("This object is already on the map!");

        Objects.Add(clonedObject.ObjectId, clonedObject);
    }

    /// <summary>
    /// Resolve the map header <see cref="MapData.PerPlayerLoadTrigger"/> if the trigger exists
    /// as a live entity on this sector. Mirrors client FUN_004CDCC0 → FUN_004BB1C0:
    /// only fire when the COID is findable in the world object hash.
    /// COID &lt; 0 means disabled (e.g. biomek 708 has no PerPlayerLoad).
    /// </summary>
    public bool TryGetPerPlayerLoadTrigger(out Trigger trigger)
    {
        trigger = null;
        var trigCoid = MapData.PerPlayerLoadTrigger;
        if (trigCoid < 0)
            return false;

        if (GetObjectByCoid(trigCoid) is Trigger live
            && live.Template is not null
            && live.Template.Reactions.Count > 0)
        {
            trigger = live;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Retail-aligned on-load grant: if PerPlayerLoad trigger is findable, fire its reaction list
    /// (e.g. 707: 16217 → GiveMission 14137 → mission 554). Server tracks via
    /// <see cref="Reaction"/> handlers and notifies client with 0x206C.
    /// Call after Stage3 create packets with <see cref="Character"/> as activator.
    /// </summary>
    /// <returns>True if the trigger was findable and reactions were dispatched.</returns>
    public bool FireOnLoadPlayerMissions(ClonedObjectBase activator)
    {
        if (activator is null)
            return false;

        if (!TryGetPerPlayerLoadTrigger(out var trigger))
        {
            var coid = MapData.PerPlayerLoadTrigger;
            if (coid >= 0)
            {
                Logger.WriteLog(LogType.Debug,
                    "SectorMap {0}: PerPlayerLoad trigger {1} not findable — skip on-load grant",
                    ContinentId,
                    coid);
            }
            return false;
        }

        Logger.WriteLog(LogType.Debug,
            "SectorMap {0}: firing PlayerOnLoad trigger {1} ({2} reaction(s)) for activator {3}",
            ContinentId,
            trigger.ObjectId.Coid,
            trigger.Template.Reactions.Count,
            activator.ObjectId.Coid);

        TriggerReactions(activator, trigger.Template.Reactions.ToList());
        return true;
    }

    public void LeaveMap(ClonedObjectBase clonedObject)
    {
        if (clonedObject is Trigger trigger)
            Triggers.Remove(trigger.ObjectId);

        if (clonedObject is Reaction reaction)
            Reactions.Remove(reaction.ObjectId);

        if (!Objects.ContainsKey(clonedObject.ObjectId))
            throw new InvalidOperationException("This object is not on the map!");

        Objects.Remove(clonedObject.ObjectId);

        // Clear any trigger states for this object when it leaves the map
        TriggerManager.Instance.ClearTriggersFor(clonedObject.ObjectId.Coid);
    }

    public IEnumerable<GhostObject> ObjectsInRange(GhostObject scopeObject)
    {
        // TODO: proper space partitioning, select entities based on distance!

        return Objects.Select(p => p.Value.Ghost).Where(g =>
        {
            if (g == null)
                return false;

            if (g == scopeObject) // Let itself (GhostCharacter) be in scope
                return true;

            if (g is GhostVehicle && MapData.ContinentObject.IsTown)
                return false;

            if (g is GhostCharacter && !MapData.ContinentObject.IsTown)
                return false;

            return true;
        });
    }

    public void TriggerReactions(ClonedObjectBase activator, List<long> reactions)
    {
        TriggerReactionsInternal(activator, reactions, 0);
    }

    private void TriggerReactionsInternal(ClonedObjectBase activator, List<long> reactions, int depth)
    {
        const int MaxReactionDepth = 10;
        if (depth >= MaxReactionDepth)
        {
            Logger.WriteLog(LogType.Error, $"Reaction chain exceeded max depth of {MaxReactionDepth}, stopping to prevent infinite loop");
            return;
        }

        var clientPacket = new GroupReactionCallPacket();
        GroupReactionCallPacket broadcastPacket = null;
        GroupReactionCallPacket convoyPacket = null;
        var childReactionsToTrigger = new List<List<long>>();

        foreach (var reactionCoid in reactions)
        {
            var foundObj = Objects.FirstOrDefault(o => o.Key.Coid == reactionCoid && !o.Key.Global);
            if (foundObj.Value is not Reaction reaction)
            {
                Logger.WriteLog(LogType.Error, $"Map {MapData.ContinentObject.Id} tried to trigger reaction {reactionCoid}, but the Reaction object isn't found!");
                continue;
            }

            //Logger.WriteLog(LogType.Debug, $"Processing reaction {reactionCoid} ({reaction.Template.ReactionType}), depth={depth}");

            if (reaction.TriggerIfPossible(activator))
            {
                var packet = new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, false);

                clientPacket.AddPacket(packet);

                if (reaction.Template.DoForAllPlayers)
                {
                    broadcastPacket ??= new GroupReactionCallPacket();
                    broadcastPacket.AddPacket(packet);
                }

                if (reaction.Template.DoForConvoy)
                {
                    convoyPacket ??= new GroupReactionCallPacket();
                    convoyPacket.AddPacket(packet);
                }

                // Queue child reactions for processing after this batch
                if (reaction.Template.Reactions.Count > 0)
                {
                    Logger.WriteLog(LogType.Debug, $"Reaction {reactionCoid} has {reaction.Template.Reactions.Count} child reactions to trigger");
                    childReactionsToTrigger.Add(reaction.Template.Reactions);
                }
            }
        }

        // Vehicle activators must resolve to owning character; empty batches skip send.
        // skipOpcode: true — client bitstream parser (0x6374F0) reads RPC payload from byte 0
        // as entry count; opcode is carried by TNL RPC type (same as MapInfoPacket).
        if (clientPacket.Count > 0)
        {
            var character = ResolveCharacter(activator);
            character?.OwningConnection?.SendGamePacket(clientPacket, skipOpcode: true);
        }

        // Process child reactions after sending the parent reaction packets
        foreach (var childReactions in childReactionsToTrigger)
        {
            TriggerReactionsInternal(activator, childReactions, depth + 1);
        }

        if (broadcastPacket is not null && broadcastPacket.Count > 0)
        {
            var exclude = ResolveCharacter(activator);
            foreach (var character in Objects.Values.OfType<Character>())
            {
                if (exclude is not null && ReferenceEquals(character, exclude))
                    continue;
                character.OwningConnection?.SendGamePacket(broadcastPacket, skipOpcode: true);
            }
        }

        if (convoyPacket is not null)
        {
            Logger.WriteLog(LogType.Debug, $"DoForConvoy GroupReactionCall skipped (no convoy system) on map {ContinentId}");
        }
    }

    /// <summary>Character that receives S2C reaction packets for this activator.</summary>
    internal static Character ResolveCharacter(ClonedObjectBase activator)
    {
        if (activator is null)
            return null;
        return activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
    }
}
