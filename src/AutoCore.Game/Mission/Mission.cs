using System.Diagnostics;
using System.Xml.Linq;

namespace AutoCore.Game.Mission;

using AutoCore.Game.Managers;
using AutoCore.Utils.Extensions;

public enum MissionType
{
    NonRandom = -1,
    Destroy = 0,
    Defend = 1,
    Escort = 2,
    Race = 3,
    Sneak = 4,
    Spy = 5,
    Deliver = 6,
    Collect = 7,
    Pickup = 8,
    Craft = 9
}

public class Mission
{
    #region Template Data
    public int Achievement { get; set; }
    public short ActiveObjectiveOverride { get; set; }
    public short AutoAssign { get; set; }
    public int Continent { get; set; }
    public int Discipline { get; set; }
    public int DisciplineValue { get; set; }
    public int Id { get; set; }
    public short IsRepeatable { get; set; }
    public int[] Item { get; set; }
    public short[] ItemIsKit { get; set; }
    public int[] ItemQuantity { get; set; }
    public int[] ItemTemplate { get; set; }
    public float[] ItemValue { get; set; }
    public int NPC { get; set; }
    public string Name { get; set; }
    public byte NumberOfObjectives { get; set; }

    public Dictionary<byte, MissionObjective> Objectives { get; set; }
    public int Pocket { get; set; }
    public int Priority { get; set; }
    public int Region { get; set; }
    public short ReqClass { get; set; }
    public int ReqLevelMax { get; set; }
    public int ReqLevelMin { get; set; }
    public int[] ReqMissionId { get; set; }
    public short ReqRace { get; set; }
    public int RequirementEventId { get; set; }
    public int RequirementsNegative { get; set; }
    public int RequirementsOred { get; set; }
    public int RewardDiscipline { get; set; }
    public int RewardDisciplineValue { get; set; }
    public int RewardUnassignedDisciplinePoints { get; set; }
    public short TargetLevel { get; set; }
    public byte Type { get; set; }
    #endregion

    #region Extra Data
    public string Title { get; set; }
    public string InternalName { get; set; }
    public string Description { get; set; }
    public string OnLineAccept { get; set; }
    public string OnLineReject { get; set; }
    public string NotCompleteText { get; set; }
    public string CompleteText { get; set; }
    public string FailText { get; set; }
    public bool CoreMission { get; set; }
    #endregion

    public static Mission Read(BinaryReader reader)
    {
        var mission = new Mission
        {
            Id = reader.ReadInt32(),
            Name = reader.ReadUTF16StringOn(65),
            Type = reader.ReadByte(),
            Objectives = new Dictionary<byte, MissionObjective>()
        };

        reader.BaseStream.Position += 1;

        mission.NPC = reader.ReadInt32();
        mission.Priority = reader.ReadInt32();
        mission.ReqRace = reader.ReadInt16();
        mission.ReqClass = reader.ReadInt16();
        mission.ReqLevelMin = reader.ReadInt32();
        mission.ReqLevelMax = reader.ReadInt32();
        mission.ReqMissionId = reader.ReadConstArray(4, reader.ReadInt32);
        mission.IsRepeatable = reader.ReadInt16();

        reader.BaseStream.Position += 2;

        mission.Item = reader.ReadConstArray(4, reader.ReadInt32);
        mission.ItemTemplate = reader.ReadConstArray(4, reader.ReadInt32);
        mission.ItemValue = reader.ReadConstArray(4, reader.ReadSingle);
        mission.ItemIsKit = reader.ReadConstArray(4, reader.ReadInt16);
        mission.ItemQuantity = reader.ReadConstArray(4, reader.ReadInt32);
        mission.AutoAssign = reader.ReadInt16();
        mission.ActiveObjectiveOverride = reader.ReadInt16();
        mission.Continent = reader.ReadInt32();
        mission.Achievement = reader.ReadInt32();
        mission.Discipline = reader.ReadInt32();
        mission.DisciplineValue = reader.ReadInt32();
        mission.RewardDiscipline = reader.ReadInt32();
        mission.RewardDisciplineValue = reader.ReadInt32();
        mission.RewardUnassignedDisciplinePoints = reader.ReadInt32();
        mission.RequirementEventId = reader.ReadInt32();
        mission.TargetLevel = reader.ReadInt16();

        reader.BaseStream.Position += 2;

        mission.RequirementsOred = reader.ReadInt32();
        mission.RequirementsNegative = reader.ReadInt32();
        mission.Region = reader.ReadInt32();
        mission.Pocket = reader.ReadInt32();
        mission.NumberOfObjectives = reader.ReadByte();

        reader.BaseStream.Position += 7;

        // Prefer in-memory GLM XML when available (GLM must load before WAD — see AssetManager).
        var element = TryLoadGlmMissionElement(mission.Name);

        var numOfObjective = reader.ReadInt32();
        for (var i = 0; i < numOfObjective; ++i)
        {
            var obj = MissionObjective.ReadNew(reader, mission, element);
            mission.Objectives.Add(obj.Sequence, obj);
        }

        if (element != null)
            mission.ApplyGlmMissionHeader(element);

        return mission;
    }

    /// <summary>
    /// Applies / re-applies GLM mission XML (header + objective requirements).
    /// Safe to call after a WAD/GLM race left objectives without Requirement children.
    /// </summary>
    /// <returns>True when at least one objective gained requirements (or already had them).</returns>
    internal bool ApplyGlmXml(XElement missionElement)
    {
        if (missionElement == null)
            return false;

        ApplyGlmMissionHeader(missionElement);

        var applied = false;
        foreach (var objective in Objectives.Values)
        {
            if (objective == null)
                continue;

            var objElem = missionElement.Elements("Objective")
                .FirstOrDefault(e => (uint)e.Attribute("sequence") == objective.Sequence);
            if (objElem == null)
                continue;

            if (objective.ApplyGlmXml(objElem))
                applied = true;
        }

        return applied;
    }

    /// <summary>Loads <c>{name}.xml</c> from GLMs and applies it when present.</summary>
    internal bool TryApplyGlmXmlFromAssetManager()
    {
        var element = TryLoadGlmMissionElement(Name);
        return element != null && ApplyGlmXml(element);
    }

    private void ApplyGlmMissionHeader(XElement element)
    {
        Title = (string)element.Element("Title");
        InternalName = (string)element.Element("Internal");
        Description = (string)element.Element("Description");
        OnLineAccept = (string)element.Element("OnLineAccept");
        OnLineReject = (string)element.Element("OnLineReject");
        NotCompleteText = (string)element.Element("NotCompleteText");
        CompleteText = (string)element.Element("CompleteText");
        FailText = (string)element.Element("FailText");
        CoreMission = (string)element.Element("CoreMission") != "0";
    }

    private static XElement TryLoadGlmMissionElement(string missionName)
    {
        if (string.IsNullOrEmpty(missionName))
            return null;

        var stream = AssetManager.Instance.GetFileStreamFromGLMs($"{missionName}.xml");
        if (stream == null)
            return null;

        using (stream)
        {
            var doc = XDocument.Load(stream);
            Debug.Assert(doc != null);
            return doc.Element("Mission");
        }
    }

    /// <summary>Unit-test factory.</summary>
    /// <remarks>
    /// <see cref="ReqRace"/> / <see cref="ReqClass"/> default to -1 (0xFFFF) = unrestricted,
    /// matching client <c>CVOGCharacter_CheckMissionRequirements</c>. Retail WAD always sets
    /// explicit values; 0 is a valid race/class id (Human / Commando), not "any".
    /// </remarks>
    internal static Mission CreateForTests(int id, params MissionObjective[] objectives)
    {
        var mission = new Mission
        {
            Id = id,
            Name = $"mission_{id}",
            Objectives = new Dictionary<byte, MissionObjective>(),
            NumberOfObjectives = (byte)(objectives?.Length ?? 0),
            ReqRace = -1,
            ReqClass = -1,
        };

        if (objectives != null)
        {
            foreach (var objective in objectives)
                mission.Objectives[objective.Sequence] = objective;
        }

        return mission;
    }
}
