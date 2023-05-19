namespace AutoCore.Game.TNL;

using AutoCore.Database.Char;
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

        var character = ObjectManager.Instance.GetOrLoadCharacter(packet.CharacterCoid, context);
        if (character == null)
        {
            Disconnect("Invalid character");

            return;
        }

        if (!LoginManager.Instance.LoginToSector(this, character.AccountId))
        {
            Disconnect("Invalid Username or password!");

            return;
        }

        var mapInfoPacket = new MapInfoPacket();

        var map = MapManager.Instance.GetMap(character.LastTownId);
        map.Fill(mapInfoPacket);

        SendGamePacket(mapInfoPacket, skipOpcode: true);
    }

    private void HandleTransferFromGlobalStage2(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        var character = ObjectManager.Instance.GetCharacter(packet.CharacterCoid);
        if (character == null)
        {
            Disconnect("Invalid character");

            return;
        }

        SendGamePacket(new TransferFromGlobalStage3Packet
        {
            SecurityKey = packet.SecurityKey,
            CharacterCoid = packet.CharacterCoid,
            PositionX = character.Position.X,
            PositionY = character.Position.Y,
            PositionZ = character.Position.Z
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

        if (character.Ghost == null)
            character.CreateGhost();

        SetScopeObject(character.Ghost);

        if (character.CurrentVehicle.Ghost == null)
            character.CurrentVehicle.CreateGhost();

        ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);

        // TODO: check if these packets are not sent, will the objects appear?
        // will it never enter the world?
        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
    }
}
