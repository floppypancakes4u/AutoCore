namespace AutoCore.Game.Inventory;

using AutoCore.Game.Packets.Sector;

public sealed class InventoryItemCreateResult
{
    private InventoryItemCreateResult(bool wasSuccessful, CreateSimpleObjectPacket packet, string displayName, string error)
    {
        WasSuccessful = wasSuccessful;
        Packet = packet;
        DisplayName = displayName;
        Error = error;
    }

    public bool WasSuccessful { get; }
    public CreateSimpleObjectPacket Packet { get; }
    public string DisplayName { get; }
    public string Error { get; }

    public static InventoryItemCreateResult Success(CreateSimpleObjectPacket packet, string displayName)
    {
        return new InventoryItemCreateResult(true, packet, displayName, string.Empty);
    }

    public static InventoryItemCreateResult Unsupported(string error)
    {
        return new InventoryItemCreateResult(false, null, string.Empty, error);
    }
}
