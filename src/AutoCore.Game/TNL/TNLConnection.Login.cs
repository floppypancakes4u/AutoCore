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

        var (result, coid) = CharacterSelectionManager.CreateNewCharacter(this, packet);

        SendGamePacket(new LoginNewCharacterResponsePacket(result ? 0x80000000 : 0x1, coid));

        if (result)
        {
            CharacterSelectionManager.ExtendCharacterList(this, coid);
        }
    }

    private void HandleLoginDeleteCharacterPacket(BinaryReader reader)
    {
        var packet = new LoginDeleteCharacterPacket();
        packet.Read(reader);

        CharacterSelectionManager.DeleteCharacter(this, packet.CharacterCoid);
    }
}
