namespace AutoCore.Game.TNL;

using AutoCore.Database.Char;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;

public partial class TNLConnection
{
    private void HandleTransferFromGlobalPacket(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        // TODO: validate security key with info received from communicator or DB value or something...
        using var context = new CharContext();

        CurrentCharacter = ObjectManager.Instance.GetOrLoadCharacter(packet.CharacterCoid, context);
        if (CurrentCharacter == null)
        {
            Disconnect("Invalid character");

            return;
        }

        if (!LoginManager.Instance.LoginToSector(this, CurrentCharacter.AccountId))
        {
            Disconnect("Invalid Username or password!");

            return;
        }

        var mapInfoPacket = new MapInfoPacket();

        var map = MapManager.Instance.GetMap(CurrentCharacter.LastTownId);

        CurrentCharacter.SetOwningConnection(this);
        CurrentCharacter.GMLevel = Account.Level;
        CurrentCharacter.SetMap(map);
        CurrentCharacter.CurrentVehicle.SetMap(map);

        map.Fill(mapInfoPacket);

        SendGamePacket(mapInfoPacket, skipOpcode: true);
    }

    private void HandleTransferFromGlobalStage2Packet(BinaryReader reader)
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

    private void HandleTransferFromGlobalStage3Packet(BinaryReader reader)
    {
        var packet = new TransferFromGlobalStage3Packet();
        packet.Read(reader);

        var character = ObjectManager.Instance.GetCharacter(packet.CharacterCoid);
        if (character == null)
        {
            Disconnect("Invalid character");

            return;
        }

        if (!Ghosting)
            ActivateGhosting();

        character.CreateGhost();
        character.CurrentVehicle.CreateGhost();

        SetScopeObject(character.Ghost);

        ObjectLocalScopeAlways(character.Ghost);
        ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);

        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
    }

    private void HandleCreatureMovedPacket(BinaryReader reader)
    {
        var packet = new CreatureMovedPacket();
        packet.Read(reader);

        CurrentCharacter.HandleMovement(packet);
    }

    private void HandleVehicleMovedPacket(BinaryReader reader)
    {
        var packet = new VehicleMovedPacket();
        packet.Read(reader);

        CurrentCharacter.CurrentVehicle.HandleMovement(packet);
    }
}
