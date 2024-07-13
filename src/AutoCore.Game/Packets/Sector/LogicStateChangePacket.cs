namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public enum LogicStateChangeType
{
    Reaction = 0,
    Variable = 1
}

public class LogicStateChangePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.LogicStateChange;

    public LogicStateChangeType Type { get; set; }

    public int VariableId { get; set; }
    public float Value { get; set; }

    public long ReactionCoid { get; set; }
    public TFID Activator { get; set; }
    public bool SingleClientOnly { get; set; }

    public LogicStateChangePacket(int variableId, float value)
    {
        Type = LogicStateChangeType.Variable;
        VariableId = variableId;
        Value = value;
    }

    public LogicStateChangePacket(long reactionCoid, TFID activator, bool singleClientOnly)
    {
        ReactionCoid = reactionCoid;
        Activator = activator;
        SingleClientOnly = singleClientOnly;
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((byte)Type);

        writer.BaseStream.Position += 3;

        if (Type == LogicStateChangeType.Variable)
        {
            writer.Write(VariableId);
            writer.Write(Value);

            writer.BaseStream.Position += 24;
        }
        else if (Type == LogicStateChangeType.Reaction)
        {
            writer.Write(ReactionCoid);
            writer.WriteTFID(Activator);
            writer.Write(SingleClientOnly);

            writer.BaseStream.Position += 7;
        }
        else
            throw new InvalidDataException($"Unknown LogicStateChangeType {Type}!");
    }
}
