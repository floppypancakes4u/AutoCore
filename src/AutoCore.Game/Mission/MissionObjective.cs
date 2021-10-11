using System.IO;

namespace AutoCore.Game.Mission
{
    using Utils.Extensions;

    public class MissionObjective
    {
        public int AttribPoints { get; private set; }
        public int ContinentObject { get; private set; }
        public float CreditScaler { get; private set; }
        public int Credits { get; private set; }
        public short CreditsIndex { get; private set; }
        public byte LayerIndex { get; private set; }
        public string MapName { get; private set; }
        public int ObjectiveId { get; private set; }
        public string ObjectiveName { get; private set; }
        public int QuestId { get; private set; }
        public int ReturnToNPC { get; private set; }
        public byte Sequence { get; private set; }
        public int SkillPoints { get; private set; }
        public int WorldPosition { get; private set; }
        public int XP { get; private set; }
        public float XPBalanceScaler { get; private set; }
        public short XPIndex { get; private set; }
        public float XPScaler { get; private set; }

        #region Extra Data
        //public List<ObjectiveRequirement> Requirements { get; private set; }
        public Mission Owner { get; private set; }
        public string ExternalMapText { get; private set; }
        public string DefaultMapText { get; private set; }
        public string Title { get; private set; }
        public int CompleteCount { get; private set; }
        #endregion

        public static MissionObjective ReadNew(BinaryReader reader, Mission owner/*, XElement elem*/)
        {
            var missionObjective = new MissionObjective
            {
                QuestId = reader.ReadInt32(),
                ObjectiveId = reader.ReadInt32(),
                Sequence = reader.ReadByte(),
                Owner = owner,
                //Requirements = new List<ObjectiveRequirement>(),
            };

            reader.BaseStream.Position += 1;

            missionObjective.ObjectiveName = reader.ReadUTF16StringOn(65);
            missionObjective.MapName = reader.ReadUTF16StringOn(65);

            reader.BaseStream.Position += 2;

            missionObjective.WorldPosition = reader.ReadInt32();
            missionObjective.ContinentObject = reader.ReadInt32();
            missionObjective.LayerIndex = reader.ReadByte();

            reader.BaseStream.Position += 3;

            missionObjective.XP = reader.ReadInt32();
            missionObjective.Credits = reader.ReadInt32();
            missionObjective.AttribPoints = reader.ReadInt32();
            missionObjective.SkillPoints = reader.ReadInt32();
            missionObjective.ReturnToNPC = reader.ReadInt32();

            missionObjective.XPIndex = reader.ReadInt16();
            missionObjective.CreditsIndex = reader.ReadInt16();

            missionObjective.XPScaler = reader.ReadSingle();
            missionObjective.XPBalanceScaler = reader.ReadSingle();
            missionObjective.CreditScaler = reader.ReadSingle();

            /*Do we need this?
            var obj = elem?.Elements("Objective").SingleOrDefault(e => (uint)e.Attribute("sequence") == mo.Sequence);
            if (obj == null)
                return mo;

            mo.ExternalMapText = (string)obj.Element("ExternalText");
            mo.Title = (string)obj.Element("Title");
            mo.DefaultMapText = (string)obj.Element("DefaultText");
            var cCountElem = obj.Element("CompleteCount");
            mo.CompleteCount = (cCountElem == null || string.IsNullOrEmpty((string)cCountElem)) ? 0 : (int)cCountElem;

            var req = obj.Elements("Requirement").ToList();
            if (req.Any())
                mo.Requirements.AddRange(req.Select(xElem => ObjectiveRequirement.Create(mo, xElem)).Where(requirement => requirement != null));*/

            return missionObjective;
        }
    }
}
