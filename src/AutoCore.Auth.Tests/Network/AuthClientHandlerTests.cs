using System.Net;
using System.Text;
using AutoCore.Auth.Data;
using AutoCore.Auth.Network;
using AutoCore.Auth.Packets.Client;
using AutoCore.Auth.Packets.Server;
using AutoCore.Database.Auth;
using AutoCore.Database.Auth.Models;
using AutoCore.Utils.Packets;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Auth.Tests.Network;

[TestClass]
public class AuthClientHandlerTests
{
    private Func<AuthContext>? _prevClientFactory;
    private Func<AuthContext>? _prevServerFactory;

    [TestInitialize]
    public void Init()
    {
        _prevClientFactory = AuthClient.CreateAuthContext;
        _prevServerFactory = AuthServer.CreateAuthContext;
    }

    [TestCleanup]
    public void Cleanup()
    {
        AuthClient.CreateAuthContext = _prevClientFactory ?? (static () => new AuthContext());
        AuthServer.CreateAuthContext = _prevServerFactory ?? (static () => new AuthContext());
    }

    private static AuthContext CreateInMemory(string name) =>
        new(new DbContextOptionsBuilder<AuthContext>().UseInMemoryDatabase(name).Options);

    private static (AuthServer Server, AuthClient Client, List<IBasePacket> Sent) CreateHarness(
        string dbName,
        uint session1 = 10,
        uint session2 = 20)
    {
        AuthServer.CreateAuthContext = () => CreateInMemory(dbName);
        AuthClient.CreateAuthContext = () => CreateInMemory(dbName);

        var server = new AuthServer();
        var sent = new List<IBasePacket>();
        var client = new AuthClient(server, oneTimeKey: 77, sessionId1: session1, sessionId2: session2)
        {
            TestSendHook = p => sent.Add(p)
        };
        return (server, client, sent);
    }

    [TestMethod]
    public void CreatePacket_MapsAllClientOpcodes()
    {
        Assert.IsInstanceOfType(AuthClient.CreatePacket(ClientOpcode.Login), typeof(LoginPacket));
        Assert.IsInstanceOfType(AuthClient.CreatePacket(ClientOpcode.Logout), typeof(LogoutPacket));
        Assert.IsInstanceOfType(AuthClient.CreatePacket(ClientOpcode.AboutToPlay), typeof(AboutToPlayPacket));
        Assert.IsInstanceOfType(AuthClient.CreatePacket(ClientOpcode.ServerListExt), typeof(ServerListExtPacket));
        Assert.IsInstanceOfType(AuthClient.CreatePacket(ClientOpcode.SCCheck), typeof(SCCheckPacket));
    }

    [TestMethod]
    public void CreatePacket_UnknownOpcode_Throws()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            AuthClient.CreatePacket((ClientOpcode)0xFF));
    }

    [TestMethod]
    public void HandlePacket_SCCheck_DoesNotReply()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.HandlePacket(new SCCheckPacket { UserId = 42, CardValue = 99 });
            Assert.AreEqual(0, sent.Count);
            Assert.AreNotEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void HandlePacket_NonAuthPacket_IsIgnored()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.HandlePacket(new DummyPacket());
            Assert.AreEqual(0, sent.Count);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogin_UnknownUser_SendsLoginFailAndCloses()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.HandlePacket(new LoginPacket { UserName = "missing", Password = "x" });

            Assert.IsTrue(sent.OfType<LoginFailPacket>().Any(p => p.ResultCode == FailReason.UserNameOrPassword));
            Assert.AreEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogin_WrongPassword_SendsLoginFail()
    {
        var db = Guid.NewGuid().ToString("N");
        SeedAccount(db, "bob", "correct", locked: false);

        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.HandlePacket(new LoginPacket { UserName = "bob", Password = "wrong" });
            Assert.IsTrue(sent.OfType<LoginFailPacket>().Any());
            Assert.AreEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogin_LockedAccount_SendsBlockedAndCloses()
    {
        var db = Guid.NewGuid().ToString("N");
        SeedAccount(db, "locked", "pw", locked: true);

        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.HandlePacket(new LoginPacket { UserName = "locked", Password = "pw" });
            Assert.IsTrue(sent.OfType<BlockedAccountPacket>().Any());
            Assert.AreEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogin_Success_SendsLoginOkAndSetsState()
    {
        var db = Guid.NewGuid().ToString("N");
        SeedAccount(db, "alice", "secret", locked: false);

        var (server, client, sent) = CreateHarness(db, session1: 111, session2: 222);
        try
        {
            client.HandlePacket(new LoginPacket { UserName = "alice", Password = "secret" });

            Assert.AreEqual(ClientState.LoggedIn, client.State);
            Assert.IsNotNull(client.Account);
            Assert.AreEqual("alice", client.Account!.Username);
            var ok = sent.OfType<LoginOkPacket>().Single();
            Assert.AreEqual(111u, ok.SessionId1);
            Assert.AreEqual(222u, ok.SessionId2);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogin_SecondConnection_KicksExistingAuthClient()
    {
        var db = Guid.NewGuid().ToString("N");
        SeedAccount(db, "alice", "secret", locked: false);

        AuthServer.CreateAuthContext = () => CreateInMemory(db);
        AuthClient.CreateAuthContext = () => CreateInMemory(db);

        var server = new AuthServer();
        var firstSent = new List<IBasePacket>();
        var secondSent = new List<IBasePacket>();

        var first = new AuthClient(server, oneTimeKey: 1, sessionId1: 11, sessionId2: 12)
        {
            TestSendHook = p => firstSent.Add(p)
        };
        var second = new AuthClient(server, oneTimeKey: 2, sessionId1: 21, sessionId2: 22)
        {
            TestSendHook = p => secondSent.Add(p)
        };

        server.Clients.Add(first);
        server.Clients.Add(second);

        try
        {
            first.HandlePacket(new LoginPacket { UserName = "alice", Password = "secret" });
            Assert.AreEqual(ClientState.LoggedIn, first.State);
            Assert.IsTrue(firstSent.OfType<LoginOkPacket>().Any());

            second.HandlePacket(new LoginPacket { UserName = "alice", Password = "secret" });

            Assert.AreEqual(ClientState.LoggedIn, second.State);
            Assert.IsTrue(secondSent.OfType<LoginOkPacket>().Any());
            Assert.IsTrue(firstSent.OfType<AccountKickedPacket>().Any(),
                "Older Auth connection must receive AccountKicked.");
            Assert.AreEqual(ClientState.Disconnected, first.State,
                "Older Auth connection must be closed immediately.");
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogin_DifferentAccounts_DoNotKickEachOther()
    {
        var db = Guid.NewGuid().ToString("N");
        SeedAccount(db, "alice", "secret", locked: false);
        SeedAccount(db, "bob", "secret", locked: false);

        AuthServer.CreateAuthContext = () => CreateInMemory(db);
        AuthClient.CreateAuthContext = () => CreateInMemory(db);

        var server = new AuthServer();
        var aliceSent = new List<IBasePacket>();
        var bobSent = new List<IBasePacket>();

        var alice = new AuthClient(server, oneTimeKey: 1, sessionId1: 11, sessionId2: 12)
        {
            TestSendHook = p => aliceSent.Add(p)
        };
        var bob = new AuthClient(server, oneTimeKey: 2, sessionId1: 21, sessionId2: 22)
        {
            TestSendHook = p => bobSent.Add(p)
        };

        server.Clients.Add(alice);
        server.Clients.Add(bob);

        try
        {
            alice.HandlePacket(new LoginPacket { UserName = "alice", Password = "secret" });
            bob.HandlePacket(new LoginPacket { UserName = "bob", Password = "secret" });

            Assert.AreEqual(ClientState.LoggedIn, alice.State);
            Assert.AreEqual(ClientState.LoggedIn, bob.State);
            Assert.IsFalse(aliceSent.OfType<AccountKickedPacket>().Any());
            Assert.IsFalse(bobSent.OfType<AccountKickedPacket>().Any());
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogout_InvalidSession_DoesNotClose()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, _) = CreateHarness(db, session1: 1, session2: 2);
        try
        {
            client.Account = new Account { Id = 1, Username = "u" };
            client.HandlePacket(new LogoutPacket { SessionId1 = 9, SessionId2 = 9 });
            Assert.AreEqual(ClientState.Connected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgLogout_ValidSession_Closes()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, _) = CreateHarness(db, session1: 1, session2: 2);
        try
        {
            client.Account = new Account { Id = 1, Username = "u" };
            client.HandlePacket(new LogoutPacket { SessionId1 = 1, SessionId2 = 2 });
            Assert.AreEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgServerListExt_InvalidSession_DoesNotSendList()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db, session1: 1, session2: 2);
        try
        {
            client.Account = new Account { Id = 1, Username = "u", LastServerId = 1 };
            client.HandlePacket(new ServerListExtPacket { SessionId1 = 0, SessionId2 = 0, ListKind = 0 });
            Assert.AreEqual(0, sent.Count);
            Assert.AreNotEqual(ClientState.ServerList, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgServerListExt_ValidSession_SendsList()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db, session1: 5, session2: 6);
        try
        {
            server.Servers[1] = new AutoCore.Communicator.ServerInfo
            {
                ServerId = 1,
                Ip = IPAddress.Parse("127.0.0.1"),
                Port = 27001
            };
            // Also add Any to ensure it is filtered out
            server.Servers[2] = new AutoCore.Communicator.ServerInfo
            {
                ServerId = 2,
                Ip = IPAddress.Any
            };

            client.Account = new Account { Id = 1, Username = "u", LastServerId = 1 };
            client.HandlePacket(new ServerListExtPacket { SessionId1 = 5, SessionId2 = 6, ListKind = 0 });

            Assert.AreEqual(ClientState.ServerList, client.State);
            var list = sent.OfType<SendServerListExtPacket>().Single();
            Assert.AreEqual((byte)1, list.LastServerId);
            Assert.AreEqual(1, list.ServerList.Count());
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgAboutToPlay_InvalidSession_DoesNotRedirect()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db, session1: 1, session2: 2);
        try
        {
            client.Account = new Account { Id = 1, Username = "u", Email = "e" };
            client.HandlePacket(new AboutToPlayPacket { SessionId1 = 0, SessionId2 = 0, ServerId = 1 });
            Assert.AreEqual(0, sent.Count);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void MsgAboutToPlay_ValidSession_RequestsRedirection()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, _) = CreateHarness(db, session1: 1, session2: 2);
        try
        {
            client.Account = new Account { Id = 1, Username = "u", Email = "e@e" };
            // No communicator client for server 1 — should not throw
            client.HandlePacket(new AboutToPlayPacket { SessionId1 = 1, SessionId2 = 2, ServerId = 1 });
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void RedirectionResult_Failure_SendsPlayFailAndCloses()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.Account = new Account { Id = 1, Username = "u" };
            client.RedirectionResult(1, false);
            Assert.IsTrue(sent.OfType<PlayFailPacket>().Any(p => p.ResultCode == FailReason.UnexpectedError));
            Assert.AreEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void RedirectionResult_Success_SendsPlayOkUpdatesLastServerAndCloses()
    {
        var db = Guid.NewGuid().ToString("N");
        SeedAccount(db, "redir", "pw", locked: false);

        uint id;
        using (var ctx = CreateInMemory(db))
            id = ctx.Accounts.Single().Id;

        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.Account = new Account { Id = id, Username = "redir" };
            client.RedirectionResult(serverId: 3, result: true);

            var ok = sent.OfType<PlayOkPacket>().Single();
            Assert.AreEqual(77u, ok.OneTimeKey);
            Assert.AreEqual(id, ok.UserId);
            Assert.AreEqual((byte)3, ok.ServerId);
            Assert.AreEqual(ClientState.Disconnected, client.State);

            using var ctx = CreateInMemory(db);
            Assert.AreEqual((byte)3, ctx.Accounts.Single().LastServerId);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void Close_IsIdempotent()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, _) = CreateHarness(db);
        try
        {
            client.Close();
            client.Close();
            Assert.AreEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void Update_DispatchesQueuedIncomingPackets()
    {
        var db = Guid.NewGuid().ToString("N");
        SeedAccount(db, "queue", "pw", locked: false);
        var (server, client, sent) = CreateHarness(db, session1: 1, session2: 2);
        try
        {
            // Build a Login payload that ProcessDecryptedPayload can parse.
            // Login Read expects DES blob — use HandlePacket via queue by enqueuing through ProcessDecryptedPayload for Logout.
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)ClientOpcode.Logout);
                w.Write(1u);
                w.Write(2u);
            }

            client.Account = new Account { Id = 1, Username = "queue" };
            client.ProcessDecryptedPayload(ms.ToArray(), (int)ms.Length);
            client.Update(0);

            Assert.AreEqual(ClientState.Disconnected, client.State);
        }
        finally
        {
            server.Shutdown();
        }
    }

    [TestMethod]
    public void Update_WhenDisconnected_SkipsQueue()
    {
        var db = Guid.NewGuid().ToString("N");
        var (server, client, sent) = CreateHarness(db);
        try
        {
            client.Close();
            client.Update(0);
            Assert.AreEqual(0, sent.Count);
        }
        finally
        {
            server.Shutdown();
        }
    }

    private static void SeedAccount(string db, string user, string password, bool locked)
    {
        using var ctx = CreateInMemory(db);
        var salt = Account.CreateSalt();
        ctx.Accounts.Add(new Account
        {
            Username = user,
            Email = $"{user}@test.local",
            Salt = salt,
            Password = Account.Hash(password, salt),
            JoinDate = DateTime.UtcNow,
            Locked = locked,
            Validated = true
        });
        ctx.SaveChanges();
    }

    private sealed class DummyPacket : IBasePacket
    {
        public void Read(BinaryReader reader) { }
        public void Write(BinaryWriter writer) { }
    }
}
