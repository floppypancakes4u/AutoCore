namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// S2C DestroyObject (0x2020). Retail client: Client_RecvDestroyObject @ 0x00814A38 →
/// CompletelyDestroyObject @ 0x00944271. Non-silent <see cref="DeathType"/> plays death VFX
/// (creature path maps Violent/Overkill/Fiery/Peaceful to FX ids).
/// Absolute offsets (opcode at 0): unknown@+4, victim TFID@+8, extra@+18/+1C, guard coid@+20,
/// deathType@+28, pad@+2C, murderer TFID@+30, force@+40.
/// <para>
/// Death/respawn UI trigger: at the top of Client_RecvDestroyObject, if this packet's
/// <see cref="ObjectId"/> (not GuardCoid) matches the local player's currently-possessed
/// vehicle, the client shows the death/respawn window (FUN_00802170) and returns without
/// tearing the object down — the ghost HealthMask/corpse-bit update alone does not pop this
/// UI. CompletelyDestroyObject has an equivalent owner-character check for the vehicle-type
/// branch. GuardCoid is only consulted in a secondary "observing another entity" branch
/// (gated on a separate spectating flag), where it takes over from ObjectId as the local-avatar
/// comparison and suppresses destroy without showing UI.
/// </para>
/// </summary>
public class DestroyObjectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.DestroyObject;

    /// <summary>Reserved dword at absolute +0x04 (body +0x00). Keep 0 until RE names it.</summary>
    public int UnknownField { get; set; } = 0;

    public TFID ObjectId { get; set; }

    /// <summary>Absolute +0x18 — passed into CompletelyDestroyObject; zero until named.</summary>
    public int ExtraA { get; set; } = 0;

    /// <summary>Absolute +0x1C — passed into CompletelyDestroyObject; zero until named.</summary>
    public int ExtraB { get; set; } = 0;

    /// <summary>Absolute +0x20 (8 bytes). Client skips destroy when this matches local player COID.</summary>
    public long GuardCoid { get; set; } = 0;

    /// <summary>Absolute +0x28. Silent (0) = despawn without death FX; combat uses Violent etc.</summary>
    public DeathType DeathType { get; set; } = DeathType.Silent;

    /// <summary>Absolute +0x30. Killer TFID applied via client SetMurderer before teardown.</summary>
    public TFID Murderer { get; set; }

    /// <summary>Absolute +0x40. CompletelyDestroyObject force flag.</summary>
    public bool Force { get; set; }

    public DestroyObjectPacket()
    {
        ObjectId = new TFID();
        Murderer = new TFID();
    }

    public DestroyObjectPacket(TFID objectId)
    {
        ObjectId = objectId ?? new TFID();
        Murderer = new TFID();
    }

    public DestroyObjectPacket(TFID objectId, DeathType deathType, TFID murderer = null, bool force = false)
        : this(objectId)
    {
        DeathType = deathType;
        Murderer = murderer ?? new TFID();
        Force = force;
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(UnknownField);
        writer.WriteTFID(ObjectId ?? new TFID());
        writer.Write(ExtraA);
        writer.Write(ExtraB);
        writer.Write(GuardCoid);
        writer.Write((int)DeathType);
        writer.Write(0); // pad @ absolute +0x2C
        writer.WriteTFID(Murderer ?? new TFID());
        writer.Write(Force);
        writer.WriteZeros(3); // pad to body size 0x40
    }
}
