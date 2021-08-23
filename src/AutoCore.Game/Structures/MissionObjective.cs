using System.IO;

namespace AutoCore.Game.Structures
{
    using Utils.Extensions;

    public class MissionObjective
    {
        public int AttribPoints;
        public int ContinentObject;
        public float CreditScaler;
        public int Credits;
        public short CreditsIndex;
        public byte LayerIndex;
        public string MapName;
        public int ObjectiveId;
        public string ObjectiveName;
        public int QuestId;
        public int ReturnToNPC;
        public byte Sequence;
        public int SkillPoints;
        public int WorldPosition;
        public int XP;
        public float XPBalanceScaler;
        public short XPIndex;
        public float XPScaler;

        #region Extra Data
        //public List<ObjectiveRequirement> Requirements;
        public Mission Owner { get; private set; }
        public string ExternalMapText { get; private set; }
        public string DefaultMapText { get; private set; }
        public string Title { get; private set; }
        public int CompleteCount { get; private set; }
        #endregion

        public static MissionObjective ReadNew(BinaryReader reader, Mission owner/*, XElement elem*/)
        {
            var mo = new MissionObjective
            {
                QuestId = reader.ReadInt32(),
                ObjectiveId = reader.ReadInt32(),
                Sequence = reader.ReadByte(),
                Owner = owner,
                //Requirements = new List<ObjectiveRequirement>(),
            };

            reader.BaseStream.Position += 1;

            mo.ObjectiveName = reader.ReadUnicodeString(65);
            mo.MapName = reader.ReadUnicodeString(65);

            reader.BaseStream.Position += 2;

            mo.WorldPosition = reader.ReadInt32();
            mo.ContinentObject = reader.ReadInt32();
            mo.LayerIndex = reader.ReadByte();

            reader.BaseStream.Position += 3;

            mo.XP = reader.ReadInt32();
            mo.Credits = reader.ReadInt32();
            mo.AttribPoints = reader.ReadInt32();
            mo.SkillPoints = reader.ReadInt32();
            mo.ReturnToNPC = reader.ReadInt32();

            mo.XPIndex = reader.ReadInt16();
            mo.CreditsIndex = reader.ReadInt16();

            mo.XPScaler = reader.ReadSingle();
            mo.XPBalanceScaler = reader.ReadSingle();
            mo.CreditScaler = reader.ReadSingle();

            /*var obj = elem?.Elements("Objective").SingleOrDefault(e => (uint)e.Attribute("sequence") == mo.Sequence);
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

            return mo;
        }
    }
}
