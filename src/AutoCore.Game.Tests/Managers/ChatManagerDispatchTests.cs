using System.Text;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// Command dispatch matrix for <see cref="ChatManager"/> without map/asset dependencies.
/// </summary>
[TestClass]
public class ChatManagerDispatchTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void Init()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
    }

    private TNLConnection CreateConnection(bool withCharacter = true, bool withVehicle = false)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        connection.Account = new Account { Id = 1, Name = "chat-tester", Level = 1 };

        if (withCharacter)
        {
            var character = new Character();
            character.SetCoid(5001, true);
            // Seed minimal DB row so Name/Level/XP properties and attribute commands do not NRE.
            character.AttachTestDataForTests("chat-tester");
        character.GMLevel = 1;
            character.SetOwningConnection(connection);
            connection.CurrentCharacter = character;

            if (withVehicle)
            {
                var vehicle = new Vehicle();
                vehicle.SetCoid(5002, true);
                // CBID defaults to -1 until clone base is loaded — covers /getcbid failure path.
                character.SetCurrentVehicleForTests(vehicle);
            }
        }

        return connection;
    }

    private static BinaryReader ChatReader(string message, ChatType type = ChatType.SectorMessage, string privateName = "")
    {
        var packet = new ChatPacket
        {
            ChatType = type,
            IsGM = false,
            PrivateRecipientName = privateName ?? "",
            Sender = "tester",
            Message = message ?? "",
            MessageLength = (short)(Encoding.UTF8.GetByteCount(message ?? "") + 1)
        };

        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);
        stream.Position = 0;
        return new BinaryReader(stream);
    }

    private static BinaryReader BroadcastReader(string message)
    {
        var packet = new BroadcastPacket
        {
            ChatType = ChatType.SystemMessage,
            IsGM = false,
            Sender = "tester",
            Message = message ?? "",
            MessageLength = (short)(Encoding.UTF8.GetByteCount(message ?? "") + 1)
        };

        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);
        stream.Position = 0;
        return new BinaryReader(stream);
    }

    [TestMethod]
    public void HandleChatPacket_UnknownCommand_SendsNoResponse()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/notarealcommand"));

        Assert.AreEqual(0, _sent.Count, "Unknown slash commands return without a system response.");
    }

    [TestMethod]
    public void HandleChatPacket_ClientControlDoubleSlash_IsIgnored()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("//playerrename"));

        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleChatPacket_GetCbid_WithoutVehicle_SendsUsageMessage()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/getcbid"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a vehicle");
        Assert.AreEqual("System", broadcast.Sender);
        Assert.AreEqual(ChatType.SystemMessage, broadcast.ChatType);
    }

    [TestMethod]
    public void HandleChatPacket_GetCbid_WithUnloadedVehicle_ReportsUnable()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: true);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/GETCBID")); // case-insensitive

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Unable to get vehicle CBID");
    }

    [TestMethod]
    public void HandleChatPacket_ListItems_ViaChatCommandService_IsHandled()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/listitems"));

        // ChatCommandService handles /listitems and returns a catalog page message.
        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        Assert.IsFalse(string.IsNullOrEmpty(broadcast.Message));
        Assert.AreEqual("System", broadcast.Sender);
    }

    [TestMethod]
    public void HandleChatPacket_Loot_WithoutMap_ReportsNeedVehicle()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: true);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/loot 123"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "need a vehicle on a map");
    }

    [TestMethod]
    public void HandleChatPacket_NullCharacter_CommandDoesNotThrow()
    {
        var connection = CreateConnection(withCharacter: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/maps"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a map");
    }

    [TestMethod]
    public void HandleChatPacket_UnhandledChatType_DoesNotThrow()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(
            connection,
            ChatReader("hello world", ChatType.FactionMessage));

        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleChatPacket_PrivateMessage_MissingTarget_NoSend()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(
            connection,
            ChatReader("secret", ChatType.PrivateMessage, privateName: "NobodyOnline"));

        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleBroadcastPacket_Command_Dispatches()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleBroadcastPacket(connection, BroadcastReader("/getcbid"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a vehicle");
    }

    [TestMethod]
    public void HandleBroadcastPacket_PlainMessage_EchoesToSender()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleBroadcastPacket(connection, BroadcastReader("hello sector"));

        var echo = _sent.OfType<BroadcastPacket>().Single();
        Assert.AreEqual("hello sector", echo.Message);
    }

    [TestMethod]
    public void HandleChatPacket_EquippedItems_WithoutVehicle_ReportsError()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/equippedItems"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a vehicle");
    }

    [TestMethod]
    public void HandleChatPacket_Kill_WithoutVehicle_ReportsError()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/kill"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a vehicle");
    }

    [TestMethod]
    public void HandleChatPacket_Warp_WithoutMap_ReportsError()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/warp 1"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a map");
    }

    [TestMethod]
    public void HandleChatPacket_CombatText_WithoutVehicle_Safe()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/combattext"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Enter a vehicle first");
    }

    [TestMethod]
    public void HandleChatPacket_GetNearbyCbids_WithoutVehicle_ReportsError()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/getNearbyCBIDs"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a vehicle");
    }

    [TestMethod]
    public void HandleChatPacket_Loot_MissingArgs_WithVehicleNoMap_StillNeedsMap()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: true);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/loot"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "need a vehicle on a map");
    }

    [TestMethod]
    public void HandleChatPacket_GetXp_ReportsLevelSnapshot()
    {
        var connection = CreateConnection();
        connection.CurrentCharacter!.SetLevel(3);
        connection.CurrentCharacter.SetExperience(100);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/getxp"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Level=");
        StringAssert.Contains(broadcast.Message, "XP=");
    }

    [TestMethod]
    public void HandleChatPacket_Xp_MissingArgs_SendsUsage()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/xp"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Usage: /xp");
    }

    [TestMethod]
    public void HandleChatPacket_Xp_InvalidAmount_ReportsError()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/xp notanumber"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Invalid XP amount");
    }

    [TestMethod]
    public void HandleChatPacket_Level_MissingArgs_SendsUsage()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/level"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Usage: /level");
    }

    [TestMethod]
    public void HandleChatPacket_Level_InvalidValue_ReportsError()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/level 0"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Invalid level");
    }

    [TestMethod]
    public void HandleChatPacket_Credits_Query_ReturnsMessage()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/credits"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        Assert.IsFalse(string.IsNullOrEmpty(broadcast.Message));
    }

    [TestMethod]
    public void HandleChatPacket_Mana_MissingArgs_SendsUsage()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/mana"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Usage: /mana");
    }

    [TestMethod]
    public void HandleChatPacket_Mana_ValidValues_SendsPacketAndAck()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/mana 10 20"));

        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());
        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Set mana to 10/20");
    }

    [TestMethod]
    public void HandleChatPacket_Mana_InvalidCurrent_ReportsError()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/mana abc"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Invalid current mana");
    }

    [TestMethod]
    public void HandleChatPacket_Tech_MissingAndInvalid_ReportUsage()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/tech"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Usage: /tech");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/tech nope"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Invalid tech value");
    }

    [TestMethod]
    public void HandleChatPacket_Tech_Valid_SetsAndAcks()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: true);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/tech 12"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Set Tech to 12");
    }

    [TestMethod]
    public void HandleChatPacket_Combat_MissingInvalidAndValid()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/combat"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Usage: /combat");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/combat x"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Invalid combat value");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/combat 7"));
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Set Combat to 7");
    }

    [TestMethod]
    public void HandleChatPacket_Theory_MissingInvalidAndValid()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/theory"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Usage: /theory");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/theory x"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Invalid theory value");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/theory 3"));
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Set Theory to 3");
    }

    [TestMethod]
    public void HandleChatPacket_Perception_MissingInvalidAndValid()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/perception"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Usage: /perception");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/perception x"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Invalid perception value");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/perception 4"));
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Set Perception to 4");
    }

    [TestMethod]
    public void HandleChatPacket_AttrPoints_MissingInvalidAndValid()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/attrpoints"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Usage: /attrpoints");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/attributepoints x"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Invalid attribute points");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/attrpoints 9"));
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Set Attribute Points to 9");
    }

    [TestMethod]
    public void HandleChatPacket_Research_MissingInvalidAndValid()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/research"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Usage: /research");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/researchpoints x"));
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Invalid research points");

        _sent.Clear();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/research 5"));
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());
        StringAssert.Contains(_sent.OfType<BroadcastPacket>().Single().Message, "Set Research Points to 5");
    }

    [TestMethod]
    public void HandleChatPacket_Kill_WithVehicleNoTarget_ReportsError()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: true);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/kill"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "no target");
    }

    /// <summary>
    /// SS-36 pin: /kill runs under DamageContext.Admin, which bypasses the unified hostility
    /// gate — a same-faction (or any) latched target must still die for a GM.
    /// </summary>
    [TestMethod]
    public void HandleChatPacket_Kill_SameFactionTarget_StillKills_AdminBypass()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: true);
        var character = connection.CurrentCharacter!;
        character.Faction = 0;
        var vehicle = character.CurrentVehicle!;

        var victimOwner = new Character();
        victimOwner.SetCoid(5003, true);
        victimOwner.Faction = 0; // same race as the admin — every non-admin route denies this
        var victim = new Vehicle();
        victim.SetCoid(5004, true);
        victim.InitializeHealthForTests(50);
        victim.SetOwner(victimOwner);
        victimOwner.SetCurrentVehicleForTests(victim);
        vehicle.SetTargetObject(victim);

        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/kill"));

        Assert.IsTrue(victim.IsCorpse, "/kill (Admin context) must bypass the hostility gate");
        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Killed");
    }

    [TestMethod]
    public void HandleChatPacket_EquippedItems_WithEmptyVehicle_ReportsNone()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: true);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/equippedItems"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "No equipped vehicle items");
    }

    [TestMethod]
    public void HandleChatPacket_ConvoyAndClanMessage_DoNotThrow()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(
            connection,
            ChatReader("convoy hi", ChatType.ConvoyMessage));
        ChatManager.Instance.HandleChatPacket(
            connection,
            ChatReader("clan hi", ChatType.ClanMessage));

        // Convoy/Clan managers may no-op without membership; must not throw or require map.
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void HandleChatPacket_XpinfoAlias_Dispatches()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/xpinfo"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Level=");
    }

    [TestMethod]
    public void HandleChatPacket_CurrencyAlias_Dispatches()
    {
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/currency"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        Assert.IsFalse(string.IsNullOrEmpty(broadcast.Message));
    }

    [TestMethod]
    public void HandleChatPacket_CtAlias_DispatchesCombatText()
    {
        var connection = CreateConnection(withCharacter: true, withVehicle: false);
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/ct"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "Enter a vehicle first");
    }

    [TestMethod]
    public void HandleChatPacket_Warp_MissingMapId_AfterMapRequired()
    {
        // Without map, warp still hits the "not in a map" branch first.
        var connection = CreateConnection();
        ChatManager.Instance.HandleChatPacket(connection, ChatReader("/warp"));

        var broadcast = _sent.OfType<BroadcastPacket>().Single();
        StringAssert.Contains(broadcast.Message, "not in a map");
    }
}
