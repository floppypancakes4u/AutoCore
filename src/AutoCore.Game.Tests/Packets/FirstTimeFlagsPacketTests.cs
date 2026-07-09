using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

[TestClass]
public class FirstTimeFlagsPacketTests
{
    [TestMethod]
    public void UpdateFirstTimeFlagsRequest_ReadsFourUintsAfterOpcode()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)GameOpcode.UpdateFirstTimeFlagsRequest);
            writer.Write(0x80000001u);
            writer.Write(0x00000002u);
            writer.Write(0x00000004u);
            writer.Write(0x00000008u);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.AreEqual((uint)GameOpcode.UpdateFirstTimeFlagsRequest, reader.ReadUInt32());

        var packet = new UpdateFirstTimeFlagsRequestPacket();
        packet.Read(reader);

        Assert.AreEqual(GameOpcode.UpdateFirstTimeFlagsRequest, packet.Opcode);
        Assert.AreEqual(0x80000001u, packet.FirstFlags1);
        Assert.AreEqual(0x00000002u, packet.FirstFlags2);
        Assert.AreEqual(0x00000004u, packet.FirstFlags3);
        Assert.AreEqual(0x00000008u, packet.FirstFlags4);
        Assert.AreEqual(0x14, stream.Length);
    }

    [TestMethod]
    public void CreateCharacterExtended_WritesFirstTimeFlagsAtOffset0x8EC()
    {
        var packet = CreateMinimalExtendedPacket();
        packet.FirstTimeFlags = new uint[] { 0xAABBCCDDu, 0x11223344u, 0x55667788u, 0x99AABBCCu };

        var bytes = WritePacketWithOpcode(packet);

        Assert.AreEqual((uint)GameOpcode.CreateCharacterExtended, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(GameOpcode.CreateCharacterExtended, packet.Opcode);

        var offset = IndexOfUInt32(bytes, 0xAABBCCDDu);
        Assert.AreEqual(CreateCharacterExtendedPacket.FirstTimeFlagsPacketOffset, offset);
        Assert.AreEqual(0xAABBCCDDu, BitConverter.ToUInt32(bytes, offset));
        Assert.AreEqual(0x11223344u, BitConverter.ToUInt32(bytes, offset + 4));
        Assert.AreEqual(0x55667788u, BitConverter.ToUInt32(bytes, offset + 8));
        Assert.AreEqual(0x99AABBCCu, BitConverter.ToUInt32(bytes, offset + 12));
        Assert.AreEqual(CreateCharacterExtendedPacket.FixedPacketSizeIncludingOpcode, bytes.Length);
    }

    [TestMethod]
    public void CreateCharacterExtended_QuickBarAndCreditsAnchors()
    {
        var packet = CreateMinimalExtendedPacket();
        packet.QuickBarItemCoids[0] = unchecked((long)0x0123456789ABCDEFul);
        packet.QuickBarSkills[0] = 0x0F0E0D0C;
        packet.Credits = 0x3333;

        var bytes = WritePacketWithOpcode(packet);
        Assert.AreEqual(unchecked((long)0x0123456789ABCDEFul), BitConverter.ToInt64(bytes, 0x410));
        Assert.AreEqual(0x0F0E0D0C, BitConverter.ToInt32(bytes, 0x730));
        Assert.AreEqual(0x3333L, BitConverter.ToInt64(bytes, 0x8C0));
    }

    [TestMethod]
    public void CreateCharacterExtended_WritesNonNullContinentEntry()
    {
        var packet = CreateMinimalExtendedPacket();
        packet.ContinentUnlocked[0] = new CharacterExploration
        {
            ContinentId = 42,
            ExploredBits = 0xDEADBEEF,
        };

        var bytes = WritePacketWithOpcode(packet);
        // Continents start at 0x1B8; entry = int id + byte + 3 pad + uint explored
        Assert.AreEqual(42, BitConverter.ToInt32(bytes, 0x1B8));
        Assert.AreEqual(1, bytes[0x1BC]);
        Assert.AreEqual(0xDEADBEEFu, BitConverter.ToUInt32(bytes, 0x1C0));
        Assert.AreEqual(CreateCharacterExtendedPacket.FixedPacketSizeIncludingOpcode, bytes.Length);
        // FirstTimeFlags region still zeros at 0x8EC when unset
        Assert.AreEqual(0u, BitConverter.ToUInt32(bytes, CreateCharacterExtendedPacket.FirstTimeFlagsPacketOffset));
    }

    [TestMethod]
    public void CreateCharacterExtended_VariableTailsExtendPacket()
    {
        var packet = CreateMinimalExtendedPacket();
        packet.NumSkills = 2;
        packet.NumCompletedQuests = 3;
        packet.NumAchievements = 1;
        packet.NumDisciplines = 2;
        packet.NumCurrentQuests = 1;
        packet.CurrentQuests = new List<CharacterQuest> { new(91001, 0) };

        var bytes = WritePacketWithOpcode(packet);
        var expectedExtra = 8 * 2 + 4 * 3 + 4 * 1 + 12 * 2 + 72; // skills + quests + ach + disc + current quest
        Assert.AreEqual(CreateCharacterExtendedPacket.FixedPacketSizeIncludingOpcode + expectedExtra, bytes.Length);
        // FirstTimeFlags still at fixed offset (before variable tails)
        Assert.AreEqual(0u, BitConverter.ToUInt32(bytes, CreateCharacterExtendedPacket.FirstTimeFlagsPacketOffset));
    }

    [TestMethod]
    public void CreateCharacter_BasePacketSizeIs0x1A8()
    {
        var packet = new CreateCharacterPacket
        {
            ObjectId = new TFID(1, true),
            Name = "Test",
            ClanName = "",
            CustomizedName = "",
            Position = new Vector3(0, 0, 0),
            Rotation = Quaternion.Default,
        };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)GameOpcode.CreateCharacter);
            packet.Write(writer);
            stream.SetLength(stream.Position);
        }

        Assert.AreEqual(0x1A8, stream.Length);
    }

    [TestMethod]
    public void FirstTimeFlags_TipBitsAndHideTips()
    {
        var flags = new uint[4];

        FirstTimeFlags.SetTipSeen(flags, 0);
        FirstTimeFlags.SetTipSeen(flags, 6);
        FirstTimeFlags.SetTipSeen(flags, 32);
        FirstTimeFlags.SetTipSeen(flags, 48);

        Assert.IsTrue(FirstTimeFlags.IsTipSeen(flags, 0));
        Assert.IsTrue(FirstTimeFlags.IsTipSeen(flags, 6));
        Assert.IsTrue(FirstTimeFlags.IsTipSeen(flags, 32));
        Assert.IsTrue(FirstTimeFlags.IsTipSeen(flags, 48));
        Assert.IsFalse(FirstTimeFlags.IsTipSeen(flags, 1));

        Assert.IsFalse(FirstTimeFlags.GetHideTips(flags[0]));
        flags[0] = FirstTimeFlags.SetHideTips(flags[0], true);
        Assert.IsTrue(FirstTimeFlags.GetHideTips(flags[0]));
        Assert.IsTrue(FirstTimeFlags.IsTipSeen(flags, 0));

        flags[0] = FirstTimeFlags.SetHideTips(flags[0], false);
        Assert.IsFalse(FirstTimeFlags.GetHideTips(flags[0]));
        Assert.IsTrue(FirstTimeFlags.IsTipSeen(flags, 0));
    }

    [TestMethod]
    public void FirstTimeFlags_InvalidInputsAreNoOps()
    {
        Assert.IsFalse(FirstTimeFlags.IsTipSeen(null, 0));
        Assert.IsFalse(FirstTimeFlags.IsTipSeen(new uint[2], 0));
        Assert.IsFalse(FirstTimeFlags.IsTipSeen(new uint[4], -1));
        Assert.IsFalse(FirstTimeFlags.IsTipSeen(new uint[4], 128));

        FirstTimeFlags.SetTipSeen(null, 0);
        FirstTimeFlags.SetTipSeen(new uint[2], 0);
        var flags = new uint[4];
        FirstTimeFlags.SetTipSeen(flags, -1);
        FirstTimeFlags.SetTipSeen(flags, 128);
        Assert.AreEqual(0u, flags[0]);

        FirstTimeFlags.CopyToBuffer(null, 1, 2, 3, 4);
        FirstTimeFlags.CopyToBuffer(new uint[2], 1, 2, 3, 4);

        FirstTimeFlags.Assign(out var a, out var b, out var c, out var d, 9, 8, 7, 6);
        Assert.AreEqual(9u, a);
        Assert.AreEqual(8u, b);
        Assert.AreEqual(7u, c);
        Assert.AreEqual(6u, d);

        Assert.IsFalse(FirstTimeFlags.IsValidFlagsArray(null));
        Assert.IsFalse(FirstTimeFlags.IsValidFlagsArray(new uint[3]));
        Assert.IsTrue(FirstTimeFlags.IsValidFlagsArray(new uint[4]));
        Assert.AreEqual(0x31, FirstTimeFlags.MaxTipId);
        Assert.AreEqual(4, FirstTimeFlags.DwordCount);
    }

    [TestMethod]
    public void FirstTimeFlagsAccountSync_CopyAndApply()
    {
        var dest = new uint[4];
        Assert.IsFalse(FirstTimeFlagsAccountSync.TryCopyToPacketFlags(null, dest));
        Assert.AreEqual(0u, dest[0]);

        Assert.IsFalse(FirstTimeFlagsAccountSync.TryCopyToPacketFlags(null, null));
        Assert.IsFalse(FirstTimeFlagsAccountSync.TryCopyToPacketFlags(new Account(), new uint[2]));

        var account = new Account
        {
            Id = 7,
            FirstFlags1 = 0x11,
            FirstFlags2 = 0x22,
            FirstFlags3 = 0x33,
            FirstFlags4 = 0x44,
        };
        Assert.IsTrue(FirstTimeFlagsAccountSync.TryCopyToPacketFlags(account, dest));
        CollectionAssert.AreEqual(new uint[] { 0x11, 0x22, 0x33, 0x44 }, dest);

        var db = new Account { Id = 7 };
        var session = new Account { Id = 7 };
        Assert.IsFalse(FirstTimeFlagsAccountSync.TryApplyUpdate(null, session, 1, 2, 3, 4));
        Assert.IsTrue(FirstTimeFlagsAccountSync.TryApplyUpdate(db, session, 0xA, 0xB, 0xC, 0xD));
        Assert.AreEqual(0xAu, db.FirstFlags1);
        Assert.AreEqual(0xBu, db.FirstFlags2);
        Assert.AreEqual(0xCu, db.FirstFlags3);
        Assert.AreEqual(0xDu, db.FirstFlags4);
        Assert.AreEqual(0xAu, session.FirstFlags1);
        Assert.AreEqual(0xBu, session.FirstFlags2);
        Assert.AreEqual(0xCu, session.FirstFlags3);
        Assert.AreEqual(0xDu, session.FirstFlags4);

        var dbOnly = new Account { Id = 8 };
        Assert.IsTrue(FirstTimeFlagsAccountSync.TryApplyUpdate(dbOnly, null, 5, 6, 7, 8));
        Assert.AreEqual(5u, dbOnly.FirstFlags1);
    }

    [TestMethod]
    public void FirstTimeFlagsAccountSync_TryProcessRequest()
    {
        var packet = new UpdateFirstTimeFlagsRequestPacket
        {
            FirstFlags1 = 1, FirstFlags2 = 2, FirstFlags3 = 3, FirstFlags4 = 4,
        };

        Assert.IsFalse(FirstTimeFlagsAccountSync.TryProcessRequest(null, packet, _ => null, () => { }, out var err));
        Assert.AreEqual("Account is null", err);

        var session = new Account { Id = 9 };
        Assert.IsFalse(FirstTimeFlagsAccountSync.TryProcessRequest(session, null, _ => null, () => { }, out err));
        Assert.AreEqual("Packet is null", err);

        Assert.IsFalse(FirstTimeFlagsAccountSync.TryProcessRequest(session, packet, null, () => { }, out err));
        Assert.AreEqual("Persistence callbacks are null", err);

        Assert.IsFalse(FirstTimeFlagsAccountSync.TryProcessRequest(session, packet, _ => null, () => { }, out err));
        StringAssert.Contains(err, "not found");

        var db = new Account { Id = 9 };
        var saved = false;
        Assert.IsTrue(FirstTimeFlagsAccountSync.TryProcessRequest(session, packet, _ => db, () => saved = true, out err));
        Assert.IsNull(err);
        Assert.IsTrue(saved);
        Assert.AreEqual(1u, session.FirstFlags1);
        Assert.AreEqual(4u, session.FirstFlags4);
    }

    [TestMethod]
    public void Character_WriteFirstTimeFlags_CopiesFromAccount()
    {
        var character = new AutoCore.Game.Entities.Character();
        var conn = new AutoCore.Game.TNL.TNLConnection
        {
            Account = new Account
            {
                Id = 3,
                FirstFlags1 = 0xAAu,
                FirstFlags2 = 0xBBu,
                FirstFlags3 = 0xCCu,
                FirstFlags4 = 0xDDu,
            },
        };
        character.SetOwningConnection(conn);

        var packet = new CreateCharacterExtendedPacket();
        character.WriteFirstTimeFlags(packet);

        Assert.AreEqual(0xAAu, packet.FirstTimeFlags[0]);
        Assert.AreEqual(0xBBu, packet.FirstTimeFlags[1]);
        Assert.AreEqual(0xCCu, packet.FirstTimeFlags[2]);
        Assert.AreEqual(0xDDu, packet.FirstTimeFlags[3]);
    }

    [TestMethod]
    public void Character_WriteFirstTimeFlags_NullAccountZerosFlags()
    {
        var character = new AutoCore.Game.Entities.Character();
        var packet = new CreateCharacterExtendedPacket
        {
            FirstTimeFlags = new uint[] { 1, 2, 3, 4 },
        };
        character.WriteFirstTimeFlags(packet);
        CollectionAssert.AreEqual(new uint[] { 0, 0, 0, 0 }, packet.FirstTimeFlags);
    }

    [TestMethod]
    public void TNLConnection_ProcessFirstTimeFlagsRequest_SuccessAndFailures()
    {
        var conn = new AutoCore.Game.TNL.TNLConnection();

        // null session account
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write(1u); w.Write(2u); w.Write(3u); w.Write(4u);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, true);
            Assert.IsFalse(conn.ProcessFirstTimeFlagsRequest(r, _ => null, () => { }));
        }

        conn.Account = new Account { Id = 11 };
        var db = new Account { Id = 11 };
        var saved = false;

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write(0x10u); w.Write(0x20u); w.Write(0x30u); w.Write(0x40u);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, true);
            Assert.IsTrue(conn.ProcessFirstTimeFlagsRequest(r, _ => db, () => saved = true));
        }

        Assert.IsTrue(saved);
        Assert.AreEqual(0x10u, conn.Account.FirstFlags1);
        Assert.AreEqual(0x40u, conn.Account.FirstFlags4);
        Assert.AreEqual(0x10u, db.FirstFlags1);

        // not found in store
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write(1u); w.Write(2u); w.Write(3u); w.Write(4u);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, true);
            Assert.IsFalse(conn.ProcessFirstTimeFlagsRequest(r, _ => null, () => { }));
        }
    }

    [TestMethod]
    public void BinaryWriterExtensions_WriteZerosAndTfid()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.WriteZeros(0);
        writer.WriteZeros(-1);
        Assert.AreEqual(0, stream.Length);

        writer.WriteZeros(3);
        Assert.AreEqual(3, stream.Length);
        Assert.IsTrue(stream.ToArray().All(b => b == 0));

        stream.SetLength(0);
        stream.Position = 0;
        writer.WriteZeros(100); // > 64 path
        Assert.AreEqual(100, stream.Length);
        Assert.IsTrue(stream.ToArray().All(b => b == 0));

        stream.SetLength(0);
        stream.Position = 0;
        writer.WriteZeros(300); // multi-chunk path
        Assert.AreEqual(300, stream.Length);

        stream.SetLength(0);
        stream.Position = 0;
        writer.WriteTFID(1234L, true);
        var tfidBytes = stream.ToArray();
        Assert.AreEqual(16, tfidBytes.Length);
        Assert.AreEqual(1234L, BitConverter.ToInt64(tfidBytes, 0));
        Assert.AreEqual(1, tfidBytes[8]);

        stream.SetLength(0);
        stream.Position = 0;
        writer.WriteTFID(new TFID(99, false));
        Assert.AreEqual(99L, BitConverter.ToInt64(stream.ToArray(), 0));
        Assert.AreEqual(0, stream.ToArray()[8]);

        stream.SetLength(0);
        stream.Position = 0;
        writer.Write(GameOpcode.UpdateFirstTimeFlagsRequest);
        Assert.AreEqual((uint)GameOpcode.UpdateFirstTimeFlagsRequest, BitConverter.ToUInt32(stream.ToArray(), 0));
    }

    private static CreateCharacterExtendedPacket CreateMinimalExtendedPacket()
    {
        return new CreateCharacterExtendedPacket
        {
            CurrentHealth = 100,
            ObjectId = new TFID(1, true),
            Name = "Test",
            ClanName = "",
            CustomizedName = "",
            Position = new Vector3(0, 0, 0),
            Rotation = Quaternion.Default,
        };
    }

    private static byte[] WritePacketWithOpcode(CreateCharacterExtendedPacket packet)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)packet.Opcode);
            packet.Write(writer);
            stream.SetLength(stream.Position);
        }

        return stream.ToArray();
    }

    private static int IndexOfUInt32(byte[] bytes, uint value)
    {
        for (var i = 0; i <= bytes.Length - 4; ++i)
        {
            if (BitConverter.ToUInt32(bytes, i) == value)
                return i;
        }

        return -1;
    }
}
