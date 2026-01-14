namespace AutoCore.Game.Managers.Asset;

using System.Globalization;
using System.Xml.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Utils;

public static class WadXmlWorldDataLoader
{
    public static IDictionary<Tuple<byte, byte>, ConfigNewCharacter> LoadConfigNewCharacters(string wadXmlPath)
    {
        var doc = XDocument.Load(wadXmlPath);
        var section = doc.Descendants("tConfigNewCharacters").FirstOrDefault();
        if (section == null)
            return new Dictionary<Tuple<byte, byte>, ConfigNewCharacter>();

        var dict = new Dictionary<Tuple<byte, byte>, ConfigNewCharacter>();

        foreach (var row in section.Elements("row"))
        {
            var race = (byte)GetInt(row, "IDRace", defaultValue: -1);
            var @class = (byte)GetInt(row, "IDClass", defaultValue: -1);
            if (race == byte.MaxValue || @class == byte.MaxValue)
                continue;

            var config = new ConfigNewCharacter
            {
                Race = race,
                Class = @class,
                Vehicle = GetInt(row, "CBIDVehicle", defaultValue: -1),
                Trailer = GetInt(row, "CBIDTrailer", defaultValue: -1),
                Weapon = GetInt(row, "CBIDWeapon", defaultValue: -1),
                Armor = GetInt(row, "CBIDArmor", defaultValue: -1),
                PowerPlant = GetInt(row, "CBIDPowerPlant", defaultValue: -1),
                RaceItem = GetInt(row, "CBIDRaceItem", defaultValue: -1),
                StartTown = GetInt(row, "IDStartingTown", defaultValue: -1),
                StartSkill = (uint)GetInt(row, "IDStartingSkill1", defaultValue: 0),
                OptionCode = GetInt(row, "IDOptionCode", defaultValue: -1),
                SkillBattleMode1 = (uint)GetInt(row, "IDSkillBattleMode1", defaultValue: 0),
                SkillBattleMode2 = (uint)GetInt(row, "IDSkillBattleMode2", defaultValue: 0),
                SkillBattleMode3 = (uint)GetInt(row, "IDSkillBattleMode3", defaultValue: 0),
            };

            dict[Tuple.Create(race, @class)] = config;
        }

        return dict;
    }

    public static IDictionary<int, ContinentObject> LoadContinentObjects(string wadXmlPath)
    {
        var doc = XDocument.Load(wadXmlPath);
        var section = doc.Descendants("tContinentObject").FirstOrDefault();
        if (section == null)
            return new Dictionary<int, ContinentObject>();

        var dict = new Dictionary<int, ContinentObject>();

        foreach (var row in section.Elements("row"))
        {
            var id = GetInt(row, "IDContinentObject", defaultValue: -1);
            if (id <= 0)
                continue;

            var co = new ContinentObject
            {
                Id = id,
                Coordinates = GetInt(row, "intCoordinates", defaultValue: 0),
                OwningFaction = GetInt(row, "IDOwningFaction", defaultValue: 0),
                IsPersistent = GetBoolTf(row, "bitIsPersistent"),
                IsTown = GetBoolTf(row, "bitIsTown"),
                MapFileName = GetString(row, "strMapFilename") ?? string.Empty,
                Image = GetInt(row, "CBIDImage", defaultValue: -1),
                IsClientOnly = GetBoolTf(row, "bitIsClientOnly"),
                Rotation = GetFloat(row, "rlRotation", defaultValue: 0),
                Objective = GetInt(row, "IDObjective", defaultValue: -1),
                PositionX = GetFloat(row, "rlPositionX", defaultValue: 0),
                PositionZ = GetFloat(row, "rlPositionZ", defaultValue: 0),
                DisplayName = GetString(row, "strDisplayName") ?? string.Empty,
                IsArena = GetBoolTf(row, "bitIsArena"),
                MinLevel = GetInt(row, "intMinLevel", defaultValue: 0),
                MaxLevel = GetInt(row, "intMaxLevel", defaultValue: 0),
                ContestedMission = GetInt(row, "intContestedMission", defaultValue: -1),
                MaxPlayers = GetInt(row, "intMaxPlayers", defaultValue: 0),
                PlayCreateSounds = GetBoolTf(row, "bitPlayCreateSounds"),
                DropCommodities = GetBoolTf(row, "bitDropCommodities"),
                DropBrokenItems = GetBoolTf(row, "bitDropBrokenItems"),
                MinVersion = GetInt(row, "intMinVersion", defaultValue: 0),
                MaxVersion = GetInt(row, "intMaxVersion", defaultValue: 0),
            };

            dict[id] = co;
        }

        return dict;
    }

    public static IDictionary<Tuple<int, byte>, ContinentArea> LoadContinentAreas(string wadXmlPath)
    {
        var doc = XDocument.Load(wadXmlPath);
        var section = doc.Descendants("tContinentExploredAreas").FirstOrDefault();
        if (section == null)
            return new Dictionary<Tuple<int, byte>, ContinentArea>();

        var dict = new Dictionary<Tuple<int, byte>, ContinentArea>();

        foreach (var row in section.Elements("row"))
        {
            var continentObjectId = GetInt(row, "IDContinentObject", defaultValue: -1);
            var area = GetInt(row, "IDExploredArea", defaultValue: -1);
            if (continentObjectId <= 0 || area < 0 || area > byte.MaxValue)
                continue;

            var ca = new ContinentArea
            {
                ContinentObjectId = continentObjectId,
                Area = (byte)area,
                AreaName = GetString(row, "strExploredAreaName") ?? string.Empty,
                XPLevel = GetInt(row, "intXPLevel", defaultValue: 0),
            };

            dict[Tuple.Create(continentObjectId, (byte)area)] = ca;
        }

        return dict;
    }

    public static IDictionary<byte, ExperienceLevel> LoadExperienceLevels(string wadXmlPath)
    {
        var doc = XDocument.Load(wadXmlPath);
        var section = doc.Descendants("tExperienceLevel").FirstOrDefault();
        if (section == null)
            return new Dictionary<byte, ExperienceLevel>();

        var dict = new Dictionary<byte, ExperienceLevel>();

        foreach (var row in section.Elements("row"))
        {
            var level = GetInt(row, "IDLevel", defaultValue: -1);
            if (level <= 0 || level > byte.MaxValue)
                continue;

            var el = new ExperienceLevel
            {
                Level = (byte)level,
                Experience = (uint)Math.Max(0, GetInt(row, "intExperience", defaultValue: 0)),
                SkillPoints = (byte)Math.Max(0, GetInt(row, "iSkillPoints", defaultValue: 0)),
                AttributePoints = (byte)Math.Max(0, GetInt(row, "iAttributePoints", defaultValue: 0)),
                ResearchPoints = (byte)Math.Max(0, GetInt(row, "iResearchPoints", defaultValue: 0)),
            };

            dict[el.Level] = el;
        }

        return dict;
    }

    private static string? GetString(XElement row, string elementName)
        => row.Element(elementName)?.Value;

    private static int GetInt(XElement row, string elementName, int defaultValue)
    {
        var v = row.Element(elementName)?.Value;
        if (string.IsNullOrWhiteSpace(v))
            return defaultValue;

        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;

        return defaultValue;
    }

    private static float GetFloat(XElement row, string elementName, float defaultValue)
    {
        var v = row.Element(elementName)?.Value;
        if (string.IsNullOrWhiteSpace(v))
            return defaultValue;

        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            return f;

        return defaultValue;
    }

    private static bool GetBoolTf(XElement row, string elementName)
    {
        // wad.xml uses "Tr"/"Fa"
        var v = row.Element(elementName)?.Value?.Trim();
        return string.Equals(v, "Tr", StringComparison.OrdinalIgnoreCase);
    }
}



