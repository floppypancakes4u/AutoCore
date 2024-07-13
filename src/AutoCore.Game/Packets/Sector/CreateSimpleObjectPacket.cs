namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class CreateSimpleObjectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.CreateSimpleObject;

    public int CBID { get; set; }
    public long CoidStore { get; set; } = -1;
    public int CurrentHealth { get; set; }
    public int MaximumHealth { get; set; }
    public int Value { get; set; }
    public int Faction { get; set; }
    public int TeamFaction { get; set; }
    public int CustomValue { get; set; }
    public int[] Prefixes { get; } = new int[5];
    public int[] Gadgets { get; } = new int[5];
    public short[] PrefixLevels { get; } = new short[5];
    public short[] GadgetLevels { get; } = new short[5];
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public float Scale { get; set; }
    public int Quantity { get; set; }
    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }
    public bool IsCorpse { get; set; }
    public TFID ObjectId { get; set; }
    public bool WillEquip { get; set; }
    public bool IsItemLink { get; set; }
    public bool IsInInventory { get; set; }
    public byte SkillLevel1 { get; set; }
    public byte SkillLevel2 { get; set; }
    public byte SkillLevel3 { get; set; }
    public bool IsIdentified { get; set; }
    public bool PossibleMissionItem { get; set; }
    public bool TempItem { get; set; }
    public bool IsKit { get; set; }
    public bool IsInfinite { get; set; }
    public bool IsBound { get; set; }
    public ushort UsesLeft { get; set; }
    public string CustomizedName { get; set; }
    public bool MadeFromMemory { get; set; }
    public bool IsMail { get; set; }
    public short MaxGadgets { get; set; }
    public short RequiredLevel { get; set; }
    public short RequiredCombat { get; set; }
    public short RequiredPerception { get; set; }
    public short RequiredTech { get; set; }
    public short RequiredTheory { get; set; }
    public int ItemTemplateId { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(CBID);
        writer.Write(CoidStore);
        writer.Write(CurrentHealth);
        writer.Write(MaximumHealth);
        writer.Write(Value);
        writer.Write(Faction);
        writer.Write(TeamFaction);
        writer.Write(CustomValue);

        for (var i = 0; i < 5; ++i)
            writer.Write(Prefixes[i]);

        for (var i = 0; i < 5; ++i)
            writer.Write(Gadgets[i]);

        for (var i = 0; i < 5; ++i)
            writer.Write(PrefixLevels[i]);

        for (var i = 0; i < 5; ++i)
            writer.Write(GadgetLevels[i]);

        writer.Write(Position.X);
        writer.Write(Position.Y);
        writer.Write(Position.Z);
        writer.Write(Rotation.X);
        writer.Write(Rotation.Y);
        writer.Write(Rotation.Z);
        writer.Write(Rotation.W);
        writer.Write(Scale);
        writer.Write(Quantity);
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.Write(IsCorpse);

        writer.BaseStream.Position += 5;

        writer.WriteTFID(ObjectId);
        writer.Write(WillEquip);
        writer.Write(IsItemLink);
        writer.Write(IsInInventory);
        writer.Write(SkillLevel1);
        writer.Write(SkillLevel2);
        writer.Write(SkillLevel3);
        writer.Write(IsIdentified);
        writer.Write(PossibleMissionItem);
        writer.Write(TempItem);
        writer.Write(IsKit);
        writer.Write(IsInfinite);
        writer.Write(IsBound);
        writer.Write(UsesLeft);
        writer.WriteUtf8StringOn(CustomizedName, 17);
        writer.Write(MadeFromMemory);
        writer.Write(IsMail);

        writer.BaseStream.Position += 1;

        writer.Write(MaxGadgets);
        writer.Write(RequiredLevel);
        writer.Write(RequiredCombat);
        writer.Write(RequiredPerception);
        writer.Write(RequiredTech);
        writer.Write(RequiredTheory);

        writer.BaseStream.Position += 2;

        writer.Write(ItemTemplateId);

        writer.BaseStream.Position += 4;
    }

    public static void WriteEmptyPacket(BinaryWriter writer)
    {
        // CBID -1 signals that it is an empty packet
        writer.Write(-1);

        writer.BaseStream.Position += 208;
    }
}
