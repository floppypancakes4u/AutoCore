using System.Net;
using System.Reflection;
using System.Text;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Login;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Utils.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// Exercises private Login/Global handlers on <see cref="TNLConnection"/> via reflection
/// and <see cref="TNLConnection.TestPacketSink"/> (no live UDP).
/// </summary>
[TestClass]
public class TNLConnectionLoginGlobalHandlerTests
{
    private string _dbName = null!;
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void Init()
    {
        _dbName = "tnl-login-" + Guid.NewGuid().ToString("N");
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);

        LoginManager.Instance.ResetForTests();
        LoginManager.Instance.CreateContext = CreateContext;
        CharacterSelectionManager.ResetForTests();
        CharacterSelectionManager.CreateContext = CreateContext;

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        LoginManager.Instance.ResetForTests();
        CharacterSelectionManager.ResetForTests();
    }

    private CharContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CharContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new CharContext(options);
    }

    private static TNLConnection CreateClient()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));
        // Unbound interface so Disconnect() does not NRE.
        connection.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));
        return connection;
    }

    private static void InvokeHandler(TNLConnection connection, string methodName, byte[] body)
    {
        var method = typeof(TNLConnection).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method, $"Missing handler {methodName}");
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private static byte[] BuildLoginRequestBody(string username, uint userId, uint authKey)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.WriteUtf8StringOn(username, 33);
        writer.WriteUtf8StringOn("unused", 33);
        writer.BaseStream.Position += 2;
        writer.Write(userId);
        writer.Write(authKey);
        writer.Flush();
        return stream.ToArray();
    }

    [TestMethod]
    public void HandleLoginRequest_AuthFailure_SendsFailureResponse()
    {
        var client = CreateClient();
        // No ExpectLoginToGlobal → LoginToGlobal fails.
        InvokeHandler(client, "HandleLoginRequestPacket", BuildLoginRequestBody("missing", 1, 1));

        var response = _sent.OfType<LoginResponsePacket>().Single();
        Assert.AreEqual(1u, response.Result);
        Assert.IsNull(client.Account);
    }

    [TestMethod]
    public void HandleLoginRequest_Success_SendsOkResponseAndSetsAccount()
    {
        Assert.IsTrue(LoginManager.Instance.ExpectLoginToGlobal(100, "login-ok", 0xBEEFu));

        var client = CreateClient();
        InvokeHandler(client, "HandleLoginRequestPacket", BuildLoginRequestBody("login-ok", 100, 0xBEEFu));

        Assert.IsNotNull(client.Account);
        Assert.AreEqual(100u, client.Account.Id);

        var response = _sent.OfType<LoginResponsePacket>().Single();
        Assert.AreEqual(0x1000000u, response.Result);
    }

    [TestMethod]
    public void HandleNews_EchoesWelcomeWithLanguage()
    {
        var client = CreateClient();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(7u); // Language
            writer.Write(0u); // unused length field consumed by Read
        }

        InvokeHandler(client, "HandleNewsPacket", stream.ToArray());

        var news = _sent.OfType<NewsPacket>().Single();
        Assert.AreEqual(7u, news.Language);
        StringAssert.Contains(news.News, "Auto Assault");
    }

    [TestMethod]
    public void HandleDisconnect_SendsAck()
    {
        var client = CreateClient();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(true); // Intentional
            writer.Write(new byte[3]);
        }

        InvokeHandler(client, "HandleDisconnectPacket", stream.ToArray());

        Assert.IsTrue(_sent.OfType<DisconnectAckPacket>().Any());
    }

    [TestMethod]
    public void HandleConvoyMissionsRequest_NullCharacter_DoesNotSendResponse()
    {
        var client = CreateClient();
        Assert.IsNull(client.CurrentCharacter);

        InvokeHandler(client, "HandleConvoyMissionsRequest", Array.Empty<byte>());

        Assert.IsFalse(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void HandleConvoyMissionsRequest_WithCharacter_SendsQuestList()
    {
        var client = CreateClient();
        var character = new AutoCore.Game.Entities.Character();
        character.SetCoid(9001, true);
        character.CurrentQuests.Add(new CharacterQuest(1001));
        character.CurrentQuests.Add(new CharacterQuest(874));
        client.CurrentCharacter = character;

        InvokeHandler(client, "HandleConvoyMissionsRequest", new byte[] { 0x01, 0x02 });

        var response = _sent.OfType<ConvoyMissionsResponsePacket>().Single();
        Assert.IsNotNull(response.MissionIds);
        CollectionAssert.AreEqual(new[] { 1001, 874 }, response.MissionIds);
        Assert.AreEqual(9001L, response.CoidMember);
    }

    [TestMethod]
    public void HandleLoginDeleteCharacter_SoftDeletesOwnedCharacter()
    {
        const uint accountId = 200;
        const long coid = 2001;

        using (var seed = CreateContext())
        {
            seed.Accounts.Add(new Account { Id = accountId, Name = "del-user", Level = 1 });
            seed.Characters.Add(new CharacterData
            {
                Coid = coid,
                AccountId = accountId,
                Name = "ToDelete",
                Deleted = false
            });
            seed.SaveChanges();
        }

        var client = CreateClient();
        client.Account = new Account { Id = accountId, Name = "del-user" };

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0); // padding consumed by LoginDeleteCharacterPacket.Read
            writer.Write(coid);
        }

        InvokeHandler(client, "HandleLoginDeleteCharacterPacket", stream.ToArray());

        using var verify = CreateContext();
        var row = verify.Characters.Single(c => c.Coid == coid);
        Assert.IsTrue(row.Deleted);
    }

    [TestMethod]
    public void HandleLoginNewCharacter_InvalidCbid_SendsFailureResponse()
    {
        var client = CreateClient();
        client.Account = new Account { Id = 50, Name = "newchar-fail", Level = 1 };

        // LoginNewCharacterPacket has Read-only layout (no Write override).
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(999_999_001); // CBID
            writer.WriteUtf8StringOn("player", 33);
            writer.WriteUtf8StringOn("FailChar", 51);
            writer.Write(0); // HeadId
            writer.Write(0); // BodyId
            writer.Write(0); // HeadDetail1
            writer.Write(0); // HeadDetail2
            writer.Write(0); // HelmetId
            writer.Write(0); // EyesId
            writer.Write(0); // MouthId
            writer.Write(0); // HairId
            writer.Write(0u); // PrimaryColor
            writer.Write(0u); // SecondaryColor
            writer.Write(0u); // EyesColor
            writer.Write(0u); // HairColor
            writer.Write(0u); // SkinColor
            writer.Write(0u); // SpecialityColor
            writer.Write(0); // ShardId
            writer.Write(0u); // VehiclePrimaryColor
            writer.Write(0u); // VehicleSecondaryColor
            writer.Write((byte)0); // VehicleTrim
            writer.Write(new byte[3]);
            writer.Write(1.0f); // ScaleOffset
            writer.Write(0); // WheelsetCBID
            writer.WriteUtf8StringOn("FailVeh", 33);
        }

        InvokeHandler(client, "HandleLoginNewCharacterPacket", stream.ToArray());

        var response = _sent.OfType<LoginNewCharacterResponsePacket>().Single();
        Assert.AreEqual(0x1u, response.Result);
    }
}
