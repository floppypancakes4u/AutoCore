namespace AutoCore.Sector.Dev;

using AutoCore.Game.TNL;

public sealed class DevConnectedCharacter
{
    public DevConnectedCharacter(long connectionId, string accountName, string characterName, long characterCoid, TNLConnection connection)
    {
        ConnectionId = connectionId;
        AccountName = accountName;
        CharacterName = characterName;
        CharacterCoid = characterCoid;
        Connection = connection;
    }

    public long ConnectionId { get; }
    public string AccountName { get; }
    public string CharacterName { get; }
    public long CharacterCoid { get; }
    public TNLConnection Connection { get; }
}
