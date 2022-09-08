namespace AutoCore.Game.Managers.Asset;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Prefixes;
using AutoCore.Game.Constants;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;
using AutoCore.Utils;

public class WADLoader
{
    public Dictionary<int, CloneBase> CloneBases { get; } = new();
    public Dictionary<int, Mission> Missions { get; } = new();
    public Dictionary<int, Skill> Skills { get; } = new();
    public Dictionary<int, PrefixBase> ArmorPrefixes { get; } = new();
    public Dictionary<int, PrefixBase> PowerPlantPrefixes { get; } = new();
    public Dictionary<int, PrefixBase> WeaponPrefixes { get; } = new();
    public Dictionary<int, PrefixBase> VehiclePrefixes { get; } = new();
    public Dictionary<int, PrefixBase> OrnamentPrefixes { get; } = new();
    public Dictionary<int, PrefixBase> RaceItemPrefixes { get; } = new();

    public bool Load(string filePath)
    {
        try
        {
            return LoadInternal(filePath);
        }
        catch (Exception e)
        {
            Logger.WriteLog(LogType.Error, $"Encountered exception while loading WAD file: {e}");
            return false;
        }
    }

    private bool LoadInternal(string filePath)
    {
        using var reader = new BinaryReader(new FileStream(filePath, FileMode.Open, FileAccess.Read));

        var version = reader.ReadUInt32();
        if (version != 27)
            return false;

        var objectCount = reader.ReadUInt32();
        for (var i = 0U; i < objectCount; ++i)
        {
            var type = reader.ReadUInt32();
            CloneBaseObject cb = (CloneBaseObjectType)type switch
            {
                CloneBaseObjectType.Object or CloneBaseObjectType.ObjectGraphicsPhysics or CloneBaseObjectType.QuestObject or
                CloneBaseObjectType.Item or CloneBaseObjectType.Store or CloneBaseObjectType.EnterPoint or
                CloneBaseObjectType.ExitPoint or CloneBaseObjectType.ContinentObject or CloneBaseObjectType.Convoy or
                CloneBaseObjectType.SpawnPoint or CloneBaseObjectType.Trigger or CloneBaseObjectType.Reaction or
                CloneBaseObjectType.MapModulePlacement or CloneBaseObjectType.MapPath or CloneBaseObjectType.Money or
                CloneBaseObjectType.Outpost => new CloneBaseObject(reader),
                CloneBaseObjectType.Commodity => new CloneBaseCommodity(reader),
                CloneBaseObjectType.Character => new CloneBaseCharacter(reader),
                CloneBaseObjectType.Weapon or CloneBaseObjectType.Bullet => new CloneBaseWeapon(reader),
                CloneBaseObjectType.Gadget => new CloneBaseGadget(reader),
                CloneBaseObjectType.TinkeringKit => new CloneBaseTinkeringKit(reader),
                CloneBaseObjectType.Vehicle => new CloneBaseVehicle(reader),
                CloneBaseObjectType.PowerPlant => new CloneBasePowerPlant(reader),
                CloneBaseObjectType.WheelSet => new CloneBaseWheelSet(reader),
                CloneBaseObjectType.Creature => new CloneBaseCreature(reader),
                CloneBaseObjectType.Armor => new CloneBaseArmor(reader),
                _ => throw new Exception("Invalid CloneBaseObjectType found!"),
            };
            CloneBases.Add(cb.CloneBaseSpecific.CloneBaseId, cb);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {objectCount} CloneBases!");

        var missionCount = reader.ReadUInt32();
        for (var i = 0U; i < missionCount; ++i)
        {
            var q = Mission.Read(reader);

            Missions.Add(q.Id, q);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {missionCount} Missions!");

        var skillCount = reader.ReadUInt32();
        for (var i = 0U; i < skillCount; ++i)
        {
            var s = Skill.Read(reader);

            Skills.Add(s.Id, s);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {skillCount} Skills!");

        var armorPrefCount = reader.ReadInt32();
        for (var i = 0; i < armorPrefCount; ++i)
        {
            var pa = new PrefixArmor(reader);

            ArmorPrefixes.Add(pa.Id, pa);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {armorPrefCount} Armor prefixes!");

        var powerPlantPrefCount = reader.ReadInt32();
        for (var i = 0; i < powerPlantPrefCount; ++i)
        {
            var ppp = new PrefixPowerPlant(reader);

            PowerPlantPrefixes.Add(ppp.Id, ppp);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {powerPlantPrefCount} Power Plant prefixes!");

        var weaponPrefCount = reader.ReadInt32();
        for (var i = 0; i < weaponPrefCount; ++i)
        {
            var pw = new PrefixWeapon(reader);

            WeaponPrefixes.Add(pw.Id, pw);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {weaponPrefCount} Weapon prefixes!");

        var vehiclePrefCount = reader.ReadInt32();
        for (var i = 0; i < vehiclePrefCount; ++i)
        {
            var pv = new PrefixVehicle(reader);

            VehiclePrefixes.Add(pv.Id, pv);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {vehiclePrefCount} Vehicle prefixes!");

        var ornamentPrefCount = reader.ReadInt32();
        for (var i = 0; i < ornamentPrefCount; ++i)
        {
            var po = new PrefixOrnament(reader);

            OrnamentPrefixes.Add(po.Id, po);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {ornamentPrefCount} Ornament prefixes!");

        var raceItemPrefCount = reader.ReadInt32();
        for (var i = 0U; i < raceItemPrefCount; ++i)
        {
            var pri = new PrefixRaceItem(reader);

            RaceItemPrefixes.Add(pri.Id, pri);
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {raceItemPrefCount} Race Item prefixes!");

        if (reader.BaseStream.Position != reader.BaseStream.Length)
        {
            Logger.WriteLog(LogType.Error, "Failed to read clonebase.wad successfully!");
            return false;
        }

        return true;
    }
}
