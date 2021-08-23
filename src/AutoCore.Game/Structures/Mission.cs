using System.Collections.Generic;
using System.IO;

namespace AutoCore.Game.Structures
{
    using Utils.Extensions;

    public class Mission
    {
        #region Template Data
        public int Achievement;
        public short ActiveObjectiveOverride;
        public short AutoAssing;
        public int Continent;
        public int Discipline;
        public int DisciplineValue;
        public int Id;
        public short IsRepeatable;
        public int[] Item;
        public short[] ItemIsKit;
        public int[] ItemQuantity;
        public int[] ItemTemplate;
        public float[] ItemValue;
        public int NPC;
        public string Name;
        public byte NumberOfObjectives;

        public Dictionary<byte, MissionObjective> Objectives;
        public int Pocket;
        public int Priority;
        public int Region;
        public short ReqClass;
        public int ReqLevelMax;
        public int ReqLevelMin;
        public int[] ReqMissionId;
        public short ReqRace;
        public int RequirementEventId;
        public int RequirementsNegative;
        public int RequirementsOred;
        public int RewardDiscipline;
        public int RewardDisciplineValue;
        public int RewardUnassignedDisciplinePoints;
        public short TargetLevel;
        public byte Type;
        #endregion

        #region Extra Data
        public string Title;
        public string InternalName;
        public string Description;
        public string OnLineAccept;
        public string OnLineReject;
        public string NotCompleteText;
        public string CompleteText;
        public string FailText;
        public bool CoreMission;
        #endregion

        public static Mission Read(BinaryReader reader)
        {
            var mi = new Mission
            {
                Id = reader.ReadInt32(),
                Name = reader.ReadUnicodeString(65),
                Type = reader.ReadByte(),
                Objectives = new Dictionary<byte, MissionObjective>()
            };

            reader.BaseStream.Position += 1;

            mi.NPC = reader.ReadInt32();
            mi.Priority = reader.ReadInt32();
            mi.ReqRace = reader.ReadInt16();
            mi.ReqClass = reader.ReadInt16();
            mi.ReqLevelMin = reader.ReadInt32();
            mi.ReqLevelMax = reader.ReadInt32();
            mi.ReqMissionId = reader.ReadConstArray(4, reader.ReadInt32);
            mi.IsRepeatable = reader.ReadInt16();

            reader.BaseStream.Position += 2;

            mi.Item = reader.ReadConstArray(4, reader.ReadInt32);
            mi.ItemTemplate = reader.ReadConstArray(4, reader.ReadInt32);
            mi.ItemValue = reader.ReadConstArray(4, reader.ReadSingle);
            mi.ItemIsKit = reader.ReadConstArray(4, reader.ReadInt16);
            mi.ItemQuantity = reader.ReadConstArray(4, reader.ReadInt32);
            mi.AutoAssing = reader.ReadInt16();
            mi.ActiveObjectiveOverride = reader.ReadInt16();
            mi.Continent = reader.ReadInt32();
            mi.Achievement = reader.ReadInt32();
            mi.Discipline = reader.ReadInt32();
            mi.DisciplineValue = reader.ReadInt32();
            mi.RewardDiscipline = reader.ReadInt32();
            mi.RewardDisciplineValue = reader.ReadInt32();
            mi.RewardUnassignedDisciplinePoints = reader.ReadInt32();
            mi.RequirementEventId = reader.ReadInt32();
            mi.TargetLevel = reader.ReadInt16();

            reader.BaseStream.Position += 2;

            mi.RequirementsOred = reader.ReadInt32();
            mi.RequirementsNegative = reader.ReadInt32();
            mi.Region = reader.ReadInt32();
            mi.Pocket = reader.ReadInt32();
            mi.NumberOfObjectives = reader.ReadByte();

            reader.BaseStream.Position += 7;

            /*XElement element = null;

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
                        mi.Title = (string)element.Element("Title");
                        mi.InternalName = (string)element.Element("Internal");
                        mi.Description = (string)element.Element("Description");
                        mi.OnLineAccept = (string)element.Element("OnLineAccept");
                        mi.OnLineReject = (string)element.Element("OnLineReject");
                        mi.NotCompleteText = (string)element.Element("NotCompleteText");
                        mi.CompleteText = (string)element.Element("CompleteText");
                        mi.FailText = (string)element.Element("FailText");
                        mi.CoreMission = (string)element.Element("CoreMission") != "0";
                    }
                }
            }*/

            var numOfObjective = reader.ReadInt32();
            for (var i = 0; i < numOfObjective; ++i)
            {
                var obj = MissionObjective.ReadNew(reader, mi/*, element*/);
                mi.Objectives.Add(obj.Sequence, obj);
            }

            return mi;
        }
    }
}
