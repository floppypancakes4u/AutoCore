using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCore.Launcher.Bootstrap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Launcher.Tests;

[TestClass]
public class PlayerCountHttpEndpointTests
{
    [TestMethod]
    public async Task Get_Root_ReturnsPlayersJson()
    {
        using var endpoint = PlayerCountHttpEndpoint.TryStart(0, () => 7);
        Assert.IsNotNull(endpoint);

        using var http = new HttpClient();
        var json = await http.GetFromJsonAsync<JsonElement>($"http://127.0.0.1:{endpoint!.BoundPort}/");

        Assert.AreEqual(7, json.GetProperty("players").GetInt32());
    }

    [TestMethod]
    public async Task Get_UnknownPath_Returns404()
    {
        using var endpoint = PlayerCountHttpEndpoint.TryStart(0, () => 0);
        Assert.IsNotNull(endpoint);

        using var http = new HttpClient();
        using var response = await http.GetAsync($"http://127.0.0.1:{endpoint!.BoundPort}/nope");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public void TryStart_PortInUse_ReturnsNull()
    {
        using var first = PlayerCountHttpEndpoint.TryStart(0, () => 0);
        Assert.IsNotNull(first);

        var second = PlayerCountHttpEndpoint.TryStart(first!.BoundPort, () => 0);
        Assert.IsNull(second);
    }

    [TestMethod]
    public void TryStart_NullCountProvider_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => PlayerCountHttpEndpoint.TryStart(0, null!));
    }
}
