using System.Diagnostics;

namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;
using System.Linq;

public class SpawnPointTemplate : GraphicsObjectTemplate
{
    public float Radius { get; set; }
    public float RespawnTime { get; set; }
    public float ActivationRange { get; set; }
    public bool UseGenerator { get; set; }
    public bool HasChampion { get; set; }
    public byte ChampionChance { get; set; }
    public byte SpawnChance { get; set; }
    public bool RandomlyOffsetSpawnPosition { get; set; }
    public List<SpawnList> Spawns { get; } = new();
    public int Loot { get; set; }
    public float LootPercent { get; set; }
    public long MapPathCoid { get; set; }
    public float InitialPatrolDistance { get; set; }
    public bool FactionDirty { get; set; }
    public int OriginalFaction { get; set; }
    public float LootChance { get; set; }
    public string MaybeChampionName { get; set; }

    public SpawnPointTemplate()
        : base(GraphicsObjectType.Graphics)
    {
    }

    public override ClonedObjectBase Create()
    {
        var spawnPoint = new SpawnPoint(this)
        {
            Layer = Layer,
            Position = Location.ToVector3(),
            Rotation = Rotation,
            // Reaction Create / map placement must carry authored Faction (FactionDirty → OriginalFaction).
            Faction = Faction,
        };

        // TODO: moar fields?

        return spawnPoint;
    }

    public override void Read(BinaryReader reader, int mapVersion)
    {
        ReadTriggerEvents(reader, mapVersion);

        Location = Vector4.ReadNew(reader);
        Rotation = Quaternion.Read(reader);
        Radius = reader.ReadSingle();
        RespawnTime = reader.ReadSingle();
        ActivationRange = reader.ReadSingle();
        UseGenerator = reader.ReadBoolean();
        HasChampion = reader.ReadBoolean();
        ChampionChance = reader.ReadByte();
        SpawnChance = reader.ReadByte();
        IsActive = reader.ReadBoolean();
        // Shared MapData is mutated by Create/Activate if they write IsActive; keep fam default.
        OriginalIsActive = IsActive;

        if (mapVersion >= 31)
            RandomlyOffsetSpawnPosition = reader.ReadBoolean();

        if (mapVersion >= 29)
        {
            for (var i = 0; i < 12; ++i)
                Spawns.Add(SpawnList.Read(reader));
        }
        else
            Debug.Assert(false, "Should be unreachable!");

        Loot = reader.ReadInt32();
        LootPercent = reader.ReadSingle();
        MapPathCoid = reader.ReadInt64();
        InitialPatrolDistance = reader.ReadSingle();

        if (mapVersion >= 15)
        {
            FactionDirty = reader.ReadBoolean();
            OriginalFaction = reader.ReadInt32();
            ApplyFactionDirtyAuthoredFaction();
        }

        if (mapVersion >= 24)
            LootChance = reader.ReadSingle();

        if (mapVersion >= 32)
            MaybeChampionName = reader.ReadLengthedString();
    }

    /// <summary>
    /// When <see cref="FactionDirty"/>, promote fam <see cref="OriginalFaction"/> onto
    /// <see cref="ObjectTemplate.Faction"/> so map placement and
    /// <c>ApplySpawnFactionOverride</c> see the authored race id (not default Human 0).
    /// </summary>
    internal void ApplyFactionDirtyAuthoredFaction()
    {
        if (FactionDirty)
            Faction = OriginalFaction;
    }

    private Random TempRandom = new();
    public SpawnList GetSpawn()
    {
        // TODO: not 100% chance to spawn?

        var realSpawns = Spawns.Where(s => s.SpawnType != -1);
        if (!realSpawns.Any())
            return null;

        if (realSpawns.Count() == 1)
            return Spawns.FirstOrDefault(s => s.SpawnType != -1);

        return realSpawns.Skip(TempRandom.Next(0, realSpawns.Count())).Take(1).FirstOrDefault();
    }

    /// <summary>
    /// Retail <c>FUN_00566490</c> population for one 12-byte spawn slot.
    /// Upper==0 and Lower==0 is the unauthored C# default (one child).
    /// Upper==0 with an authored Lower skips the slot. Both counts cap at 10.
    /// </summary>
    public static int ResolveSlotPopulationTarget(SpawnList slot, Random rng)
    {
        if (slot == null || slot.SpawnType == -1)
            return 0;

        var lower = (int)slot.LowerNumberOfSpawns;
        var upper = (int)slot.UpperNumberOfSpawns;
        if (upper == 0 && lower == 0)
            return 1;
        if (upper == 0)
            return 0;

        if (lower > 10)
            lower = 10;
        if (upper > 10)
            upper = 10;
        if (upper < lower)
            upper = lower;
        if (lower == upper)
            return lower;

        rng ??= Random.Shared;
        return rng.Next(lower, upper + 1);
    }

    /// <summary>Sum of per-slot minima used for expected-vs-actual map counts.</summary>
    public int ExpectedMinimumChildren()
    {
        var total = 0;
        foreach (var slot in Spawns)
        {
            if (slot.SpawnType == -1)
                continue;
            if (slot.UpperNumberOfSpawns == 0 && slot.LowerNumberOfSpawns == 0)
            {
                total += 1;
                continue;
            }

            if (slot.UpperNumberOfSpawns == 0)
                continue;

            total += Math.Min((int)slot.LowerNumberOfSpawns, 10);
        }

        return total;
    }

    /// <summary>
    /// Why <see cref="ExpectedMinimumChildren"/> is 0 — for spawn-failure warnings.
    /// Distinguishes an empty FAM list from typed slots that retail skips (Upper==0).
    /// </summary>
    public string DescribeUnfilledSlots()
    {
        if (Spawns.Count == 0)
            return "reason=no spawn list slots=0 emptyType=0 skippedUpper0=0";

        var emptyType = 0;
        var skippedUpper0 = 0;
        var typed = new List<string>();
        foreach (var slot in Spawns)
        {
            if (slot.SpawnType == -1)
            {
                emptyType++;
                continue;
            }

            if (slot.UpperNumberOfSpawns == 0 && slot.LowerNumberOfSpawns != 0)
                skippedUpper0++;

            typed.Add(
                $"type={slot.SpawnType} template={slot.IsTemplate} lower={slot.LowerNumberOfSpawns} upper={slot.UpperNumberOfSpawns}");
        }

        string reason;
        if (emptyType == Spawns.Count)
            reason = "all slots SpawnType=-1";
        else if (typed.Count > 0 && skippedUpper0 == typed.Count)
            reason = "typed slots have Upper=0";
        else
            reason = "expected minimum is 0";

        var typedPart = typed.Count == 0 ? string.Empty : " " + string.Join("; ", typed);
        return $"reason={reason} slots={Spawns.Count} emptyType={emptyType} skippedUpper0={skippedUpper0}{typedPart}";
    }

    public class SpawnList
    {
        public bool IsTemplate { get; set; }

        /// <summary>SS-40: signed fam byte — retail authors negative offsets (0xFF = −1).</summary>
        public sbyte LevelOffset;
        public byte LowerNumberOfSpawns;
        public int SpawnType;
        public byte UpperNumberOfSpawns;

        public static SpawnList Read(BinaryReader reader)
        {
            var spawnList = new SpawnList
            {
                LowerNumberOfSpawns = reader.ReadByte(),
                UpperNumberOfSpawns = reader.ReadByte()
            };

            reader.BaseStream.Position += 2;

            spawnList.SpawnType = reader.ReadInt32();
            spawnList.LevelOffset = unchecked((sbyte)reader.ReadByte()); // SS-40: signed on the wire
            spawnList.IsTemplate = reader.ReadBoolean();

            reader.BaseStream.Position += 2;

            return spawnList;
        }
    }
}
