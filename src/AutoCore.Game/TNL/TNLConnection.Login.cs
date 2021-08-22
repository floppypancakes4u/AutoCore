using System.IO;

namespace AutoCore.Game.TNL
{
    using Managers;
    using Packets.Login;
    using Utils;

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

            Logger.WriteLog(LogType.Network, "Client ({3} -> {1} | {2}) authenticated from {0}", GetNetAddressString(), Account.Id, Account.Name, _playerCOID);

            CharacterSelectionManager.Instance.SendCharacterList(this);

            SendGamePacket(new LoginResponsePacket(0x1000000));
        }

        private void HandleNewCharacterPacket(BinaryReader reader)
        {
            var packet = new NewCharacterPacket();
            packet.Read(reader);

            var (result, coid) = CharacterSelectionManager.Instance.CreateNewCharacter(this, packet);

            SendGamePacket(new NewCharacterResponsePacket(result ? 0x80000000 : 0x1, coid));
        }
    }
}
