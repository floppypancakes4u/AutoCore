using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sector.Tests.Dev;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Sector.Dev;

[TestClass]
public class DevControlServerTests
{
    [TestMethod]
    public void Start_EphemeralPort_IsRunningAndResolvesPort()
    {
        using var host = new DevControlHost(() => null);

        Assert.IsTrue(host.Server.IsRunning);
        Assert.IsTrue(host.Server.Port > 0, "Port 0 must resolve to the OS-assigned local port.");
    }

    [TestMethod]
    public void Start_WhenAlreadyRunning_IsIdempotent()
    {
        using var host = new DevControlHost(() => null);
        var port = host.Server.Port;

        host.Server.Start(0);

        Assert.AreEqual(port, host.Server.Port);
        Assert.IsTrue(host.Server.IsRunning);
    }

    [TestMethod]
    public void Stop_WhenNotStarted_DoesNotThrow()
    {
        var server = new DevControlServer(() => null);
        server.Stop();
        Assert.IsFalse(server.IsRunning);
    }

    [TestMethod]
    public async Task Health_WhenNoInterface_ReturnsOkWithEmptyCharacters()
    {
        using var host = new DevControlHost(() => null);

        var (status, body) = await host.SendAsync("GET", "/health");

        Assert.AreEqual(200, status);
        using var doc = JsonDocument.Parse(body);
        Assert.IsTrue(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(host.Server.Port, doc.RootElement.GetProperty("port").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("connectedCharacters").GetArrayLength());
    }

    [TestMethod]
    public async Task UnknownPath_Returns404()
    {
        using var host = new DevControlHost(() => null);

        var (status, body) = await host.SendAsync("GET", "/not-a-real-endpoint");

        Assert.AreEqual(404, status);
        StringAssert.Contains(body, "Unknown dev endpoint");
    }

    [TestMethod]
    public async Task Inventory_WhenNoConnectedCharacters_Returns400()
    {
        using var host = new DevControlHost(() => null);

        var (status, body) = await host.SendAsync("GET", "/inventory");

        Assert.AreEqual(400, status);
        StringAssert.Contains(body, "No connected characters");
    }

    [TestMethod]
    public async Task InventoryGrabLog_GetAndDelete_RoundTrip()
    {
        InventoryGrabDebugLog.Clear();
        using var host = new DevControlHost(() => null);

        var (getStatus, getBody) = await host.SendAsync("GET", "/inventory-grab-log");
        Assert.AreEqual(200, getStatus);
        StringAssert.Contains(getBody, "entries");

        var (deleteStatus, deleteBody) = await host.SendAsync("DELETE", "/inventory-grab-log");
        Assert.AreEqual(200, deleteStatus);
        StringAssert.Contains(deleteBody, "\"ok\":true");
    }

    [TestMethod]
    public async Task InventoryDropLog_GetAndDelete_RoundTrip()
    {
        InventoryDropDebugLog.Clear();
        using var host = new DevControlHost(() => null);

        var (getStatus, getBody) = await host.SendAsync("GET", "/inventory-drop-log");
        Assert.AreEqual(200, getStatus);
        StringAssert.Contains(getBody, "entries");

        var (deleteStatus, deleteBody) = await host.SendAsync("DELETE", "/inventory-drop-log");
        Assert.AreEqual(200, deleteStatus);
        StringAssert.Contains(deleteBody, "\"ok\":true");
    }

    [TestMethod]
    public async Task ChatCommand_InvalidJsonBody_Returns400()
    {
        using var host = new DevControlHost(() => null);

        var (status, body) = await host.SendAsync("POST", "/chat-command", "{not-json");

        Assert.AreEqual(400, status);
        StringAssert.Contains(body, "error");
    }

    [TestMethod]
    public async Task ChatCommand_EmptyCommand_Returns400()
    {
        using var host = new DevControlHost(() => null);

        var (status, body) = await host.SendAsync(
            "POST",
            "/chat-command",
            """{"character":"x","command":""}""");

        Assert.AreEqual(400, status);
        StringAssert.Contains(body, "Command is required");
    }

    [TestMethod]
    public async Task ChatCommand_NoConnectedCharacters_Returns400()
    {
        using var host = new DevControlHost(() => null);

        var (status, body) = await host.SendAsync(
            "POST",
            "/chat-command",
            """{"command":"/give 1"}""");

        Assert.AreEqual(400, status);
        StringAssert.Contains(body, "No connected characters");
    }

    [TestMethod]
    public void Stop_AfterStart_ClearsRunningState()
    {
        var server = new DevControlServer(() => null);
        server.Start(0);
        Assert.IsTrue(server.IsRunning);

        server.Stop();

        Assert.IsFalse(server.IsRunning);
    }

    [TestMethod]
    public void HandleRequest_Health_WithEmptyTnlInterface_ReturnsOk()
    {
        TNLInterface iface = null;
        try
        {
            iface = new TNLInterface(GetFreeUdpPort(), true);
            var server = new DevControlServer(() => iface);

            var response = server.HandleRequest(
                DevControlServer.CreateRequestForTests("GET", "/health"));

            Assert.AreEqual(200, response.StatusCode);
            StringAssert.Contains(response.Body, "connectedCharacters");
        }
        finally
        {
            iface?.Socket?.Stop();
            iface?.Close();
        }
    }

    [TestMethod]
    public void HandleRequest_HealthAndInventory_WithConnectedCharacter()
    {
        TNLInterface iface = null;
        try
        {
            iface = new TNLInterface(GetFreeUdpPort(), true);
            var conn = CreateConnectionWithCharacter(52, "Floppy", "admin");
            iface.MapConnections[1] = conn;

            var server = new DevControlServer(() => iface);

            var health = server.HandleRequest(
                DevControlServer.CreateRequestForTests("GET", "/health"));
            Assert.AreEqual(200, health.StatusCode);
            StringAssert.Contains(health.Body, "Floppy");
            StringAssert.Contains(health.Body, "admin");

            var inventory = server.HandleRequest(
                DevControlServer.CreateRequestForTests("GET", "/inventory", query: "character=Floppy"));
            Assert.AreEqual(200, inventory.StatusCode);
            StringAssert.Contains(inventory.Body, "Floppy");
            StringAssert.Contains(inventory.Body, "items");

            // Default selection when only one character is connected
            var inventoryDefault = server.HandleRequest(
                DevControlServer.CreateRequestForTests("GET", "/inventory"));
            Assert.AreEqual(200, inventoryDefault.StatusCode);
        }
        finally
        {
            iface?.Socket?.Stop();
            iface?.Close();
        }
    }

    [TestMethod]
    public void HandleRequest_ChatCommand_UnsupportedCommand_Returns400()
    {
        TNLInterface iface = null;
        try
        {
            iface = new TNLInterface(GetFreeUdpPort(), true);
            iface.MapConnections[1] = CreateConnectionWithCharacter(77, "Pilot", "acct");
            var server = new DevControlServer(() => iface);

            var response = server.HandleRequest(
                DevControlServer.CreateRequestForTests(
                    "POST",
                    "/chat-command",
                    """{"character":"Pilot","command":"/definitely-not-a-real-command-xyz"}"""));

            Assert.AreEqual(400, response.StatusCode);
            StringAssert.Contains(response.Body, "Unsupported dev chat command");
        }
        finally
        {
            iface?.Socket?.Stop();
            iface?.Close();
        }
    }

    [TestMethod]
    public void HandleRequest_MissionPlan_MissingId_Returns400()
    {
        var server = new DevControlServer(() => null);
        var response = server.HandleRequest(
            DevControlServer.CreateRequestForTests("GET", "/mission-plan"));

        Assert.AreEqual(400, response.StatusCode);
        StringAssert.Contains(response.Body, "mission id");
    }

    [TestMethod]
    public void HandleRequest_MissionPlan_UnknownId_Returns400()
    {
        var server = new DevControlServer(() => null);
        var response = server.HandleRequest(
            DevControlServer.CreateRequestForTests("GET", "/mission-plan", query: "id=99999999"));

        Assert.AreEqual(400, response.StatusCode);
        StringAssert.Contains(response.Body, "Unknown mission");
    }

    [TestMethod]
    public void HandleRequest_MissionPlan_ReturnsObjectivesAndGates()
    {
        const int missionId = 88101;
        try
        {
            var obj = MissionObjective.CreateForTests(88111, 0, missionId, 1);
            obj.Requirements.Add(new ObjectiveRequirementPatrol(obj)
            {
                TargetCount = 1,
                Sequential = true,
                FirstStateSlot = 0,
            });
            var mission = Mission.CreateForTests(missionId, obj);
            mission.Continent = 707;
            mission.NPC = 12345;
            mission.ReqLevelMin = 5;
            mission.ReqMissionId = new[] { 100, -1, -1, -1 };
            mission.Title = "Plan Test Mission";
            AssetManager.Instance.SetTestMission(mission);

            var server = new DevControlServer(() => null);
            var response = server.HandleRequest(
                DevControlServer.CreateRequestForTests("GET", "/mission-plan", query: $"id={missionId}"));

            Assert.AreEqual(200, response.StatusCode);
            using var doc = JsonDocument.Parse(response.Body);
            var root = doc.RootElement;
            Assert.IsTrue(root.GetProperty("ok").GetBoolean());
            Assert.AreEqual(missionId, root.GetProperty("missionId").GetInt32());
            Assert.AreEqual(707, root.GetProperty("continent").GetInt32());
            Assert.AreEqual(12345, root.GetProperty("npc").GetInt32());
            Assert.AreEqual(5, root.GetProperty("reqLevelMin").GetInt32());
            Assert.AreEqual(100, root.GetProperty("reqMissionIds")[0].GetInt32());
            Assert.AreEqual(1, root.GetProperty("objectives").GetArrayLength());
            Assert.AreEqual("Patrol", root.GetProperty("objectives")[0].GetProperty("requirements")[0].GetProperty("type").GetString());
        }
        finally
        {
            AssetManager.Instance.ClearTestMissions();
        }
    }

    [TestMethod]
    public void HandleRequest_MissionState_WithActiveQuest()
    {
        TNLInterface iface = null;
        try
        {
            iface = new TNLInterface(GetFreeUdpPort(), true);
            var conn = CreateConnectionWithCharacter(88, "MissionPilot", "acct");
            var quest = new CharacterQuest(88101, 0);
            quest.ObjectiveProgress[0] = 2;
            quest.ObjectiveMax[0] = 5;
            conn.CurrentCharacter.CurrentQuests.Add(quest);
            conn.CurrentCharacter.CompletedMissionIds.Add(42);
            conn.CurrentCharacter.SetLevel(7);
            iface.MapConnections[1] = conn;

            var server = new DevControlServer(() => iface);
            var response = server.HandleRequest(
                DevControlServer.CreateRequestForTests("GET", "/mission-state", query: "character=MissionPilot"));

            Assert.AreEqual(200, response.StatusCode);
            using var doc = JsonDocument.Parse(response.Body);
            var root = doc.RootElement;
            Assert.IsTrue(root.GetProperty("ok").GetBoolean());
            Assert.AreEqual(7, root.GetProperty("level").GetInt32());
            Assert.AreEqual(88101, root.GetProperty("activeQuests")[0].GetProperty("missionId").GetInt32());
            Assert.AreEqual(2, root.GetProperty("activeQuests")[0].GetProperty("progress").GetInt32());
            Assert.AreEqual(5, root.GetProperty("activeQuests")[0].GetProperty("max").GetInt32());
            Assert.AreEqual(42, root.GetProperty("completedMissionIds")[0].GetInt32());
        }
        finally
        {
            iface?.Socket?.Stop();
            iface?.Close();
        }
    }

    private static TNLConnection CreateConnectionWithCharacter(long coid, string name, string accountName)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.AttachTestDataForTests(name);

        var conn = new TNLConnection();
        conn.SetPlayerCOID(coid);
        conn.CurrentCharacter = character;
        conn.Account = new Account { Name = accountName };
        return conn;
    }

    [TestMethod]
    public void HandleRequest_InventoryQuery_MissingCharacter_Returns400()
    {
        var server = new DevControlServer(() => null);
        var response = server.HandleRequest(
            DevControlServer.CreateRequestForTests("GET", "/inventory", query: "character=Ghost"));

        Assert.AreEqual(400, response.StatusCode);
        StringAssert.Contains(response.Body, "No connected character named");
        StringAssert.Contains(response.Body, "Ghost");
    }

    [TestMethod]
    public void HandleRequest_ChatCommand_NullBodyDeserializesToError()
    {
        var server = new DevControlServer(() => null);
        // empty object → command required
        var response = server.HandleRequest(
            DevControlServer.CreateRequestForTests("POST", "/chat-command", "{}"));

        Assert.AreEqual(400, response.StatusCode);
    }

    [TestMethod]
    public void HandleRequest_UnknownMethodOnKnownPath_Returns404()
    {
        var server = new DevControlServer(() => null);
        var response = server.HandleRequest(
            DevControlServer.CreateRequestForTests("PUT", "/health"));

        Assert.AreEqual(404, response.StatusCode);
    }

    [TestMethod]
    public async Task Health_WithQueryString_StillWorks()
    {
        using var host = new DevControlHost(() => null);
        var (status, _) = await host.SendAsync("GET", "/health?x=1");
        Assert.AreEqual(200, status);
    }

    private static int GetFreeUdpPort()
    {
        using var udp = new System.Net.Sockets.UdpClient(0);
        return ((System.Net.IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    private sealed class DevControlHost : IDisposable
    {
        public DevControlServer Server { get; }

        public DevControlHost(Func<TNLInterface> getInterface)
        {
            Server = new DevControlServer(getInterface);
            Server.Start(0);
            // Give accept loop a moment to start.
            Thread.Sleep(25);
        }

        public async Task<(int Status, string Body)> SendAsync(string method, string path, string body = null)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, Server.Port).ConfigureAwait(false);
            using var stream = client.GetStream();

            var payload = body ?? string.Empty;
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var request =
                $"{method} {path} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{Server.Port}\r\n" +
                "Connection: close\r\n" +
                $"Content-Length: {payloadBytes.Length}\r\n" +
                "Content-Type: application/json\r\n" +
                "\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(headerBytes).ConfigureAwait(false);
            if (payloadBytes.Length > 0)
                await stream.WriteAsync(payloadBytes).ConfigureAwait(false);

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            var raw = Encoding.UTF8.GetString(ms.ToArray());
            return ParseHttp(raw);
        }

        private static (int Status, string Body) ParseHttp(string raw)
        {
            var split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            Assert.IsTrue(split > 0, $"Expected HTTP response, got: {raw}");
            var header = raw[..split];
            var body = raw[(split + 4)..];
            var statusLine = header.Split("\r\n")[0];
            var parts = statusLine.Split(' ', 3);
            Assert.IsTrue(parts.Length >= 2, statusLine);
            return (int.Parse(parts[1]), body);
        }

        public void Dispose()
        {
            Server.Stop();
        }
    }
}
