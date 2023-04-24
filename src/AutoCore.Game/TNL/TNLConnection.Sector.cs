namespace AutoCore.Game.TNL;

using AutoCore.Game.Packets.Sector;

public partial class TNLConnection
{
    private void HandleTransferFromGlobal(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        SendGamePacket(new MapInfoPacket());
    }

    private void HandleTransferFromGlobalStage2(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        SendGamePacket(new TransferFromGlobalStage3Packet());
    }

    private void HandleTransferFromGlobalStage3(BinaryReader reader)
    {
        var packet = new TransferFromGlobalStage3Packet();
        packet.Read(reader);

        SendGamePacket(new CreateVehicleExtendedPacket());
        SendGamePacket(new CreateCharacterExtendedPacket());
    }
}
