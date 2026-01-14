namespace AutoCore.Game.Map;

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

            // TODO: do we need ghosts of local objects at all?
            if (template.Value is GraphicsObjectTemplate)
            {
                // TODO: most likely not all object will need a ghost!
                //obj.CreateGhost();
            }

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

    public void LeaveMap(ClonedObjectBase clonedObject)
    {
        if (clonedObject is Trigger trigger)
            Triggers.Remove(trigger.ObjectId);

        if (clonedObject is Reaction reaction)
            Reactions.Remove(reaction.ObjectId);

        if (!Objects.ContainsKey(clonedObject.ObjectId))
            throw new InvalidOperationException("This object is not on the map!");

        Objects.Remove(clonedObject.ObjectId);
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

            Logger.WriteLog(LogType.Debug, $"Processing reaction {reactionCoid} ({reaction.Template.ReactionType}), depth={depth}");

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

        activator.GetAsCharacter()?.OwningConnection.SendGamePacket(clientPacket);

        // Process child reactions after sending the parent reaction packets
        foreach (var childReactions in childReactionsToTrigger)
        {
            TriggerReactionsInternal(activator, childReactions, depth + 1);
        }

        if (broadcastPacket is not null)
        {
            // TODO: broadcast packet to map
        }

        if (convoyPacket is not null)
        {
            // TODO: broadcast packet to convoy
        }
    }
}
