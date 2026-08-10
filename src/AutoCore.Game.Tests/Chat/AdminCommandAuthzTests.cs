using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Utils.Logging;

/// <summary>
/// SS-28 tripwire: mutating chat/GM commands require GMLevel &gt;= 1.
/// Reverting the gate in ChatCommandService/ChatManager must fail these tests.
/// </summary>
[TestClass]
public class AdminCommandAuthzTests
{
    private InMemoryLogSink _sink = null!;
    private readonly List<AutoCore.Game.Packets.BasePacket> _sent = new();

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        TNLConnection.TestPacketSink = null;
    }

    [TestMethod]
    public void MutatingCommand_GmLevel0_Denied_NoStateChange_EmitsSec001()
    {
        var (conn, character) = Create(gmLevel: 0, credits: 100);
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, "/addItem 1");

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(100L, character.Credits);
        var denied = _sink.Single("AdminCommandDenied");
        Assert.AreEqual("SEC-001", denied.GetProperty("ErrorCode"));
        Assert.AreEqual(0, _sink.Records.Count(r => r.EventName == "AdminCommandExecuted"));
    }

    [TestMethod]
    public void MutatingCommand_GmLevel1_Allowed_EmitsAdminCommandExecuted()
    {
        var (conn, character) = Create(gmLevel: 1, credits: 100);
        _sink.Clear();

        // /setHP is mutating and does not require map/assets when no vehicle — still authorizes.
        var result = ChatCommandService.Instance.Execute(character, "/setHP 1");

        Assert.IsTrue(result.Handled);
        var executed = _sink.Single("AdminCommandExecuted");
        Assert.IsTrue(executed.Audit);
        Assert.AreEqual(1, Convert.ToInt32(executed.GetProperty("GMLevel")));
        Assert.AreEqual(0, _sink.Records.Count(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    public void PlayerFacingCommand_ReportBug_GmLevel0_StillAllowed()
    {
        var (conn, character) = Create(gmLevel: 0, credits: 100);
        _sink.Clear();

        // /reportbug is intentionally open to all players (not GM-gated).
        var result = ChatCommandService.Instance.Execute(character, "/reportbug test");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, _sink.Records.Count(r => r.EventName == "AdminCommandDenied"));
        Assert.IsFalse(result.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("/listItems")]
    [DataRow("/showMissions")]
    [DataRow("/mission 1")]
    [DataRow("/cargoinfo")]
    [DataRow("/tptowaypoint")]
    [DataRow("/portto Bobby")]
    [DataRow("/porttome Bobby")]
    public void DiagnosticCommands_ViaService_GmLevel0_Denied(string command)
    {
        var (conn, character) = Create(gmLevel: 0, credits: 100);
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, command);

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    [DataRow("/maps")]
    [DataRow("/warp 1")]
    [DataRow("/kill")]
    [DataRow("/getcbid")]
    [DataRow("/equippedItems")]
    [DataRow("/getnearbycbids")]
    [DataRow("/getxp")]
    [DataRow("/xpinfo")]
    [DataRow("/mana 10")]
    [DataRow("/tech 1")]
    [DataRow("/combat 1")]
    [DataRow("/theory 1")]
    [DataRow("/perception 1")]
    [DataRow("/attrpoints 1")]
    [DataRow("/research 1")]
    [DataRow("/currency")]
    [DataRow("/experience 1")]
    [DataRow("/combattext")]
    [DataRow("/ct")]
    [DataRow("/level 2")]
    [DataRow("/xp 1")]
    [DataRow("/credits")]
    public void MostSlashCommands_ViaChatManager_GmLevel0_Denied(string command)
    {
        var (conn, character) = Create(gmLevel: 0, credits: 100);
        _sink.Clear();

        using var reader = ChatReader(command);
        ChatManager.Instance.HandleChatPacket(conn, reader);

        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"),
            $"Expected AdminCommandDenied for {command}");
        var broadcast = _sent.OfType<BroadcastPacket>().LastOrDefault();
        Assert.IsNotNull(broadcast, $"Expected system reply for {command}");
        Assert.IsTrue(broadcast.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase),
            $"Expected permission denial message for {command}, got: {broadcast.Message}");
    }

    [TestMethod]
    public void FallbackLoot_GmLevel0_DeniedViaChatManager()
    {
        var (conn, character) = Create(gmLevel: 0, credits: 100);
        _sink.Clear();

        using var reader = ChatReader("/loot 1");
        ChatManager.Instance.HandleChatPacket(conn, reader);

        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    [DataRow("/kick someone")]
    [DataRow("/ban someone")]
    [DataRow("/unban someone")]
    [DataRow("/listplayers")]
    public void ModerationCommands_GmLevel0_Denied(string command)
    {
        var (conn, character) = Create(gmLevel: 0, credits: 100);
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, command);

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(result.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    public void ModerationCommands_GmLevel1_ListPlayers_Allowed()
    {
        var (conn, character) = Create(gmLevel: 1, credits: 100);
        // Avoid touching live ObjectManager / Auth DB: inject empty online list.
        PlayerModerationService.Instance.ListOnline = () => Array.Empty<OnlinePlayerSnapshot>();
        try
        {
            _sink.Clear();
            var result = ChatCommandService.Instance.Execute(character, "/listplayers");
            Assert.IsTrue(result.Handled);
            Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandExecuted"));
            Assert.IsFalse(result.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            PlayerModerationService.Instance.ResetForTests();
        }
    }

    static (TNLConnection Conn, Character Character) Create(int gmLevel, long credits)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        connection.Account = new Account { Id = 9, Name = "gm-test", Level = (byte)gmLevel };

        var character = new Character();
        character.SetCoid(9001, true);
        character.AttachTestDataForTests("gm-test");
        character.SetCredits(credits);
        character.GMLevel = (byte)gmLevel;
        character.SetOwningConnection(connection);
        character.AttachInventoryForTests(new InventoryManager());
        connection.CurrentCharacter = character;
        return (connection, character);
    }

    static BinaryReader ChatReader(string message)
    {
        var packet = new ChatPacket
        {
            ChatType = ChatType.SectorMessage,
            IsGM = false,
            PrivateRecipientName = "",
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
}
