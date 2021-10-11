using System.Collections.Generic;
using System.IO;

namespace AutoCore.Game.Mission
{
    using Utils.Extensions;

    public class Mission
    {
        #region Template Data
        public int Achievement { get; set; }
        public short ActiveObjectiveOverride { get; set; }
        public short AutoAssing { get; set; }
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
            mission.AutoAssing = reader.ReadInt16();
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

            /*Do we need this?
            XElement element = null;

            var stream = AssetManager.GetStreamByName($"{mi.Name}.xml", "missions.glm") ??
                         AssetManager.GetStreamByName($"{mi.Name}.xml", "misc.glm");

            if (stream != null)
            {
                using (stream)
                {
                    var doc = XDocument.Load(stream);
                    Debug.Assert(doc != null);

                    element = doc.Element("Mission");
                    if (element != null)
                    {
                        mission.Title = (string)element.Element("Title");
                        mission.InternalName = (string)element.Element("Internal");
                        mission.Description = (string)element.Element("Description");
                        mission.OnLineAccept = (string)element.Element("OnLineAccept");
                        mission.OnLineReject = (string)element.Element("OnLineReject");
                        mission.NotCompleteText = (string)element.Element("NotCompleteText");
                        mission.CompleteText = (string)element.Element("CompleteText");
                        mission.FailText = (string)element.Element("FailText");
                        mission.CoreMission = (string)element.Element("CoreMission") != "0";
                    }
                }
            }*/

            var numOfObjective = reader.ReadInt32();
            for (var i = 0; i < numOfObjective; ++i)
            {
                var obj = MissionObjective.ReadNew(reader, mission/*, element*/);
                mission.Objectives.Add(obj.Sequence, obj);
            }

            return mission;
        }
    }
}
