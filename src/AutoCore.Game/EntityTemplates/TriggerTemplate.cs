namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class TriggerTemplate : GraphicsObjectTemplate
{
    public string Name { get; set; }
    public float RetriggerDelay { get; set; }
    public float ActivateDelay { get; set; }
    public int ActivationCount { get; set; }
    public byte TargetType { get; set; }
    public bool DoCollision { get; set; }
    public bool DoConditionals { get; set; }
    public bool ShowMapTransitionDecals { get; set; }
    public bool DoOnActivate { get; set; }
    public bool AllConditionsNeeded { get; set; }
    public bool ApplyToAllColliders { get; set; }
    public List<long> Reactions { get; } = new();
    public List<TFID> TargetList { get; } = new();
    public List<TriggerConditional> Conditions { get; } = new();
    public uint Color { get; set; }
    public uint TriggerId { get; set; }

    public TriggerTemplate()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override void Read(BinaryReader reader, int mapVersion)
    {
        Location = Vector4.ReadNew(reader);
        Rotation = Quaternion.Read(reader);
        Scale = reader.ReadSingle();
        Name = reader.ReadUTF8StringOn(64);
        RetriggerDelay = reader.ReadSingle();
        ActivateDelay = reader.ReadSingle();
        ActivationCount = reader.ReadInt32();
        TargetType = reader.ReadByte();
        DoCollision = reader.ReadBoolean();
        DoConditionals = reader.ReadBoolean();

        if (mapVersion >= 44)
            ShowMapTransitionDecals = reader.ReadBoolean();

        DoOnActivate = reader.ReadBoolean();
        AllConditionsNeeded = reader.ReadBoolean();

        if (mapVersion >= 60)
            ApplyToAllColliders = reader.ReadBoolean();

        var reactionCount = reader.ReadInt32();
        for (var i = 0; i < reactionCount; ++i)
            Reactions.Add(ReadCOIDFromFile(reader));

        var targetCount = reader.ReadInt32();
        for (var i = 0; i < targetCount; ++i)
            TargetList.Add(ReadFIDFromFile(reader));
        
        var conditionCount = reader.ReadInt32();
        for (var i = 0; i < conditionCount; ++i)
            Conditions.Add(TriggerConditional.Read(reader));

        if (mapVersion >= 9)
            Color = reader.ReadUInt32();

        if (mapVersion >= 55)
            TriggerId = reader.ReadUInt32();
    }
}
