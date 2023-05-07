namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class ReactionTemplate : ObjectTemplate
{
    public string Name { get; set; }
    public ReactionType ReactionType { get; set; }
    public bool ActOnActivator { get; set; }
    public int ObjectiveIDCheck { get; set; }
    public bool DoForConvoy { get; set; }
    public int GenericVar1 { get; set; }
    public float GenericVar2 { get; set; }
    public int GenericVar3 { get; set; }
    public MapTransferType MapTransfer { get; set; }
    public int MapTransferData { get; set; }
    public List<long> Objects { get; } = new();
    public List<long> Reactions { get; } = new();
    public ReactionText Text { get; set; }
    public bool AllConditionsNeeded { get; set; }
    public bool DoForAllPlayers { get; set; }
    public List<TriggerConditional> Conditions { get; } = new();
    public string MiscText { get; set; }
    public ReactionWaypointType WaypointType { get; set; }
    public string WaypointText { get; set; }
    public List<int> MissionTypes { get; } = new();
    public List<int> Missions { get; } = new();

    public override void Read(BinaryReader reader, int mapVersion)
    {
        Name = reader.ReadUTF8StringOn(65);
        ReactionType = (ReactionType)reader.ReadByte();
        ActOnActivator = reader.ReadBoolean();
        ObjectiveIDCheck = reader.ReadInt32();
        DoForConvoy = reader.ReadBoolean();
        GenericVar1 = reader.ReadInt32();
        GenericVar2 = reader.ReadSingle();
        GenericVar3 = reader.ReadInt32();

        if (ReactionType == ReactionType.TransferMap)
        {
            MapTransfer = (MapTransferType)reader.ReadByte();
            MapTransferData = reader.ReadInt32();
        }
        else
        {
            var objectCount = reader.ReadInt32();
            for (var i = 0; i < objectCount; ++i)
                Objects.Add(ReadCOIDFromFile(reader));
        }

        var reactionCount = reader.ReadInt32();
        for (var i = 0; i < reactionCount; ++i)
            Reactions.Add(ReadCOIDFromFile(reader));

        if (ReactionType == ReactionType.Text && reader.ReadBoolean())
            Text = ReactionText.Read(reader, mapVersion);

        if (mapVersion >= 8)
        {
            AllConditionsNeeded = reader.ReadBoolean();

            var conditionCount = reader.ReadInt32();
            for (var i = 0; i < conditionCount; ++i)
                Conditions.Add(TriggerConditional.Read(reader));

            DoForAllPlayers = reader.ReadBoolean();
        }

        if (mapVersion >= 9 && (ReactionType == ReactionType.TimerStart || ReactionType == ReactionType.TimerStop || ReactionType == ReactionType.PlayMusic || ReactionType == ReactionType.Path))
            MiscText = reader.ReadLengthedString();

        if (mapVersion >= 10 && (ReactionType == ReactionType.AddWaypoint || ReactionType == ReactionType.SetStatusText || ReactionType == ReactionType.SetProgressBar))
        {
            WaypointType = (ReactionWaypointType)reader.ReadInt32();
            WaypointText = reader.ReadLengthedString();
        }

        if (mapVersion == 16 || (mapVersion > 16 && ReactionType == ReactionType.GiveMissionDialog))
        {
            var missionTypeCount = reader.ReadInt32();
            for (var i = 0; i < missionTypeCount; ++i)
                MissionTypes.Add(reader.ReadInt32());

            var missionCount = reader.ReadInt32();
            for (var i = 0; i < missionCount; ++i)
                Missions.Add(reader.ReadInt32());

            if (mapVersion < 20)
            {
                var count = reader.ReadInt32();

                reader.BaseStream.Position += count * 4 + 4;

                var size = reader.ReadInt32();

                if (size > 0)
                    reader.BaseStream.Position += size;
            }
        }
    }

    public class ReactionText
    {
        public ReactionTextType Type { get; set; }
        public ReactionTextTargetType TargetType { get; set; }
        public string Main { get; set; }
        public List<ReactionTextParam> Params { get; } = new();
        public List<ReactionTextChoice> Choices { get; } = new();

        public static ReactionText Read(BinaryReader reader, int mapVersion)
        {
            var text = new ReactionText
            {
                Type = (ReactionTextType)reader.ReadByte(),
                TargetType = (ReactionTextTargetType)reader.ReadByte(),
                Main = reader.ReadLengthedString()
            };

            var paramCount = reader.ReadInt32();
            for (var i = 0; i < paramCount; ++i)
                text.Params.Add(ReactionTextParam.Read(reader, mapVersion));

            var choiceCount = reader.ReadInt32();
            for (var i = 0; i < choiceCount; ++i)
                text.Choices.Add(ReactionTextChoice.Read(reader, mapVersion));

            return text;
        }
    }

    public class ReactionTextParam
    {
        public ReactionTextParamType Type { get; set; }
        public int Id { get; set; }
        public float CachedValue { get; set; }

        public static ReactionTextParam Read(BinaryReader reader, int mapVersion)
        {
            var type = (ReactionTextParamType)reader.ReadByte();

            reader.BaseStream.Position += 3;

            return new ReactionTextParam
            {
                Type = type,
                Id = reader.ReadInt32(),
                CachedValue = mapVersion >= 14 ? reader.ReadSingle() : 0.0f
            };
        }
    }

    public class ReactionTextChoice
    {
        public long TriggerCoid { get; set; }
        public string Text { get; set; }
        public List<ReactionTextParam> Params { get; } = new();

        public static ReactionTextChoice Read(BinaryReader reader, int mapVersion)
        {
            var choice = new ReactionTextChoice
            {
                TriggerCoid = reader.ReadInt64(),
                Text = reader.ReadLengthedString()
            };

            var paramCount = reader.ReadInt32();
            for (var i = 0; i < paramCount; ++i)
                choice.Params.Add(ReactionTextParam.Read(reader, mapVersion));

            return choice;
        }
    }
}
