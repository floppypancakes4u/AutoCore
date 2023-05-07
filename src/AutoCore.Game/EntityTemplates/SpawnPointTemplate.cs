using System.Diagnostics;

namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

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
        }

        if (mapVersion >= 24)
            LootChance = reader.ReadSingle();

        if (mapVersion >= 32)
            MaybeChampionName = reader.ReadLengthedString();
    }

    public class SpawnList
    {
        public bool IsTemplate { get; set; }
        public byte LevelOffset;
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
            spawnList.LevelOffset = reader.ReadByte();
            spawnList.IsTemplate = reader.ReadBoolean();

            reader.BaseStream.Position += 2;

            return spawnList;
        }
    }
}
