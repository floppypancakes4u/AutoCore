namespace AutoCore.Game.Inventory;

using AutoCore.Game.Entities;
using AutoCore.Game.Packets;

public sealed class InventoryOperationResult
{
    public InventoryOperationResult(
        IReadOnlyList<BasePacket> packets,
        string logMessage = null,
        ClonedObjectBase worldObjectToDestroy = null)
    {
        Packets = packets ?? Array.Empty<BasePacket>();
        LogMessage = logMessage;
        WorldObjectToDestroy = worldObjectToDestroy;
    }

    public IReadOnlyList<BasePacket> Packets { get; }
    public string LogMessage { get; }
    public ClonedObjectBase WorldObjectToDestroy { get; }

    public static InventoryOperationResult SinglePacket(BasePacket packet, string logMessage = null)
    {
        return new InventoryOperationResult(packet == null ? Array.Empty<BasePacket>() : new[] { packet }, logMessage);
    }
}
