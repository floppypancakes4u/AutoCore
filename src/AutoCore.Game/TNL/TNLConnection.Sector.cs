namespace AutoCore.Game.TNL;

using AutoCore.Database.Char;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;

public partial class TNLConnection
{
    private void HandleTransferFromGlobal(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        // TODO: validate security key with info received from communicator or DB value or something...

        using var context = new CharContext();

        var character = new Character(this);
        if (!character.LoadFromDB(context, packet.CharacterCoid))
        {
            Disconnect("Invalid character!");
            return;
        }

        character.LoadCurrentVehicle(context);

        ObjectManager.Instance.Add(character);
        ObjectManager.Instance.Add(character.CurrentVehicle);

        var mapInfoPacket = new MapInfoPacket();

        var map = MapManager.Instance.GetMap(708);
        map.Fill(mapInfoPacket);

        SendGamePacket(mapInfoPacket, skipOpcode: true);
    }

    private void HandleTransferFromGlobalStage2(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        SendGamePacket(new TransferFromGlobalStage3Packet
        {
            SecurityKey = packet.SecurityKey,
            CharacterCoid = packet.CharacterCoid,
            PositionX = 0.0f,
            PositionY = 0.0f,
            PositionZ = 0.0f
        });
    }

    private void HandleTransferFromGlobalStage3(BinaryReader reader)
    {
        var packet = new TransferFromGlobalStage3Packet();
        packet.Read(reader);

        var character = ObjectManager.Instance.GetCharacter(packet.CharacterCoid);
        if (character == null)
        {
            Disconnect("Invalid character");

            return;
        }

        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        // hacks:
        var map = MapManager.Instance.GetMap(708);
        charPacket.Position = new(map.MapData.EntryPoint.X, map.MapData.EntryPoint.Y, map.MapData.EntryPoint.Z);
        charPacket.Rotation = new(0.0f, 0.0f, 0.0f, 1.0f);

        vehiclePacket.Position = new(map.MapData.EntryPoint.X, map.MapData.EntryPoint.Y, map.MapData.EntryPoint.Z);
        vehiclePacket.Rotation = new(0.0f, 0.0f, 0.0f, 1.0f);

        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
    }
}
