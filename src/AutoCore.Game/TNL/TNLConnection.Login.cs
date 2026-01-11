namespace AutoCore.Game.TNL;

using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Login;
using AutoCore.Utils;

public partial class TNLConnection
{
    private void HandleLoginRequestPacket(BinaryReader reader)
    {
        var packet = new LoginRequestPacket();
        packet.Read(reader);

        if (!LoginManager.Instance.LoginToGlobal(this, packet))
        {
            SendGamePacket(new LoginResponsePacket(1));
            Disconnect("Invalid Username or password!");
            return;
        }

        Logger.WriteLog(LogType.Network, "Client ({3} -> {1} | {2}) authenticated from {0}", GetNetAddressString(), Account.Id, Account.Name, PlayerCoid);

        CharacterSelectionManager.SendCharacterList(this);

        SendGamePacket(new LoginResponsePacket(0x1000000));
    }

    private void HandleLoginNewCharacterPacket(BinaryReader reader)
    {
        var packet = new LoginNewCharacterPacket();
        packet.Read(reader);
        
        Logger.WriteLog(LogType.Network, $"HandleLoginNewCharacterPacket: Received CharacterName='{packet.CharacterName}' (Length: {packet.CharacterName?.Length ?? 0}), VehicleName='{packet.VehicleName}' (Length: {packet.VehicleName?.Length ?? 0})");

        var (result, coid) = CharacterSelectionManager.CreateNewCharacter(this, packet);

        Logger.WriteLog(LogType.Network, $"HandleLoginNewCharacterPacket: Character creation result = {result}, Coid = {coid}");
        Logger.WriteLog(LogType.Network, $"HandleLoginNewCharacterPacket: Sending response with code {(result ? 0x80000000 : 0x1)}");

        SendGamePacket(new LoginNewCharacterResponsePacket(result ? 0x80000000 : 0x1, coid));

        if (result)
        {
            Logger.WriteLog(LogType.Network, $"HandleLoginNewCharacterPacket: Character creation successful, extending character list");
            CharacterSelectionManager.ExtendCharacterList(this, coid);
        }
        else
        {
            Logger.WriteLog(LogType.Error, $"HandleLoginNewCharacterPacket: Character creation failed, not extending character list");
        }
    }

    private void HandleLoginDeleteCharacterPacket(BinaryReader reader)
    {
        var packet = new LoginDeleteCharacterPacket();
        packet.Read(reader);

        CharacterSelectionManager.DeleteCharacter(this, packet.CharacterCoid);
    }
}
