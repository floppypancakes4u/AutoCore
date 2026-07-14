namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// S2C InitCreateObject (0x20B7). Client FUN_00808830:
/// <list type="bullet">
/// <item><c>bCreate=true</c> → <c>CVOGReaction_SpawnObject</c></item>
/// <item><c>bCreate=false</c> → <c>CVOGReaction_RemoveObject(..., bDoDeath)</c></item>
/// </list>
/// Retail size 0x10: opcode + bCreate + bDoDeath + pad2 + coid64.
/// <c>bDoDeath=1</c> maps to RemoveObject death path (vfunc+0x50 with Violent for types 1/3).
/// </summary>
public class InitCreateObjectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InitCreateObject;

    public bool Create { get; set; }

    /// <summary>When <see cref="Create"/> is false, non-zero plays death remove (client RemoveObject bRemoveType).</summary>
    public bool DoDeath { get; set; }

    public long ObjectCoid { get; set; }

    public InitCreateObjectPacket()
    {
    }

    public InitCreateObjectPacket(long objectCoid, bool create, bool doDeath = false)
    {
        ObjectCoid = objectCoid;
        Create = create;
        DoDeath = doDeath;
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(Create);
        writer.Write(DoDeath);
        writer.Write((ushort)0); // pad to coid at absolute +0x08
        writer.Write(ObjectCoid);
    }
}
