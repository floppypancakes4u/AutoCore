namespace AutoCore.Dev;

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

public sealed class DevControlApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public DevControlApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public Task<DevHealthResponse> GetHealthAsync()
    {
        return GetAsync<DevHealthResponse>("/health");
    }

    public Task<DevInventoryResponse> GetInventoryAsync(string character)
    {
        return GetAsync<DevInventoryResponse>($"/inventory?character={Uri.EscapeDataString(character)}");
    }

    public async Task<DevChatCommandResponse> RunChatCommandAsync(string character, string command)
    {
        var requestBody = JsonSerializer.Serialize(new { character, command }, JsonOptions);
        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(_baseUrl + "/chat-command", content);

        return await ReadResponseAsync<DevChatCommandResponse>(response);
    }

    public Task<DevInventoryGrabLogResponse> GetInventoryGrabLogAsync()
    {
        return GetAsync<DevInventoryGrabLogResponse>("/inventory-grab-log");
    }

    public Task<DevInventoryGrabLogResponse> GetInventoryDropLogAsync()
    {
        return GetAsync<DevInventoryGrabLogResponse>("/inventory-drop-log");
    }

    public async Task ClearInventoryGrabLogAsync()
    {
        var response = await _client.DeleteAsync(_baseUrl + "/inventory-grab-log");
        _ = await ReadResponseAsync<JsonElement>(response);
    }

    public async Task ClearInventoryDropLogAsync()
    {
        var response = await _client.DeleteAsync(_baseUrl + "/inventory-drop-log");
        _ = await ReadResponseAsync<JsonElement>(response);
    }

    private async Task<T> GetAsync<T>(string path)
    {
        var response = await _client.GetAsync(_baseUrl + path);
        return await ReadResponseAsync<T>(response);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = JsonSerializer.Deserialize<DevErrorResponse>(body, JsonOptions);
                throw new InvalidOperationException(error?.Error ?? body);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(body);
            }
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("Dev API returned an empty response.");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

public sealed class DevErrorResponse
{
    public string Error { get; set; } = string.Empty;
}

public sealed class DevHealthResponse
{
    public bool Ok { get; set; }
    public int Port { get; set; }
    public DevCharacterDto[] ConnectedCharacters { get; set; } = [];
}

public sealed class DevCharacterDto
{
    public long ConnectionId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public long CharacterCoid { get; set; }
    public int InventoryCount { get; set; }
}

public sealed class DevInventoryResponse
{
    public long ConnectionId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public long CharacterCoid { get; set; }
    public DevInventoryItemDto[] Items { get; set; } = [];
}

public sealed class DevChatCommandResponse
{
    public long ConnectionId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public long CharacterCoid { get; set; }
    public string Command { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DevInventoryItemDto? AddedItem { get; set; }
    public DevInventoryItemDto[] Inventory { get; set; } = [];
}

public sealed class DevInventoryItemDto
{
    public int Cbid { get; set; }
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long Coid { get; set; }
    public byte X { get; set; }
    public byte Y { get; set; }
    public int Quantity { get; set; }
}

public sealed class DevInventoryGrabLogResponse
{
    public DevInventoryGrabLogEntry[] Entries { get; set; } = [];
}

public sealed class DevInventoryGrabLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Direction { get; set; } = string.Empty;
    public int Length { get; set; }
    public string Hex { get; set; } = string.Empty;
}
