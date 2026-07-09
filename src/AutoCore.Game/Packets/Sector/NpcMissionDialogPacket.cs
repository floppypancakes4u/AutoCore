namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

/// <summary>
/// Server→client NPC mission dialog opener (opcode 0x206D).
/// Ghidra Client_RecvNpcMissionDialog @ 0x00815070 (buffer includes opcode at +0):
///   +0x08 NPC TFID (16), +0x18 count i32 (low byte used), +0x20 mission id per 40-byte entry,
///   +0x28 eight item coid slots (i32, -1 empty).
/// </summary>
public class NpcMissionDialogPacket : BasePacket
{
    public const int ItemCoidSlots = 8;
    public const int EntryStride = 40;
    public const int TfidOffset = 8;
    public const int TfidSize = 16;
    public const int CountOffset = 24;
    public const int FirstMissionOffset = 32;

    public override GameOpcode Opcode => GameOpcode.MissionDialog;

    public TFID NpcTfid { get; set; } = new();
    public List<int> MissionIds { get; } = new();
    public List<int[]> MissionItemCoids { get; } = new();

    public int PayloadLength =>
        MissionIds.Count == 0
            ? FirstMissionOffset
            : FirstMissionOffset + MissionIds.Count * EntryStride;

    public override void Write(BinaryWriter writer)
    {
        // Opcode written at +0 by SendGamePacket before Write (position=4).
        // Absolute offsets match client buffer base (opcode at +0).
        writer.BaseStream.Position = TfidOffset;
        writer.Write(NpcTfid.Coid);
        writer.Write((byte)(NpcTfid.Global ? 1 : 0));
        writer.BaseStream.Position = TfidOffset + TfidSize;

        writer.Write(MissionIds.Count);
        writer.BaseStream.Position = FirstMissionOffset;

        for (var i = 0; i < MissionIds.Count; i++)
        {
            var entryBase = FirstMissionOffset + i * EntryStride;
            writer.BaseStream.Position = entryBase;
            writer.Write(MissionIds[i]);
            writer.BaseStream.Position = entryBase + 8;

            var itemCoids = i < MissionItemCoids.Count ? MissionItemCoids[i] : null;
            for (var j = 0; j < ItemCoidSlots; j++)
            {
                var value = itemCoids != null && j < itemCoids.Length ? itemCoids[j] : -1;
                writer.Write(value);
            }
        }

        writer.BaseStream.Position = Math.Max(writer.BaseStream.Position, PayloadLength);
    }
}
