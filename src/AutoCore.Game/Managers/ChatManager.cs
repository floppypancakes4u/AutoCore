namespace AutoCore.Game.Managers;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

public class ChatManager : Singleton<ChatManager>
{
    public void HandleChatPacket(TNLConnection connection, BinaryReader reader)
    {
        var packet = new ChatPacket();
        packet.Read(reader);

        if (packet.Message.StartsWith('/'))
            return;

        switch (packet.ChatType)
        {
            case ChatType.ConvoyMessage:
                ConvoyManager.Instance.BroadcastPacket(connection.CurrentCharacter, packet);
                break;

            case ChatType.ClanMessage:
                ClanManager.Instance.BroadcastPacket(connection.CurrentCharacter, packet);
                break;

            case ChatType.PrivateMessage:
                var target = ObjectManager.Instance.GetCharacterByName(packet.PrivateRecipientName);
                if (target == null)
                    break;

                connection.SendGamePacket(packet);
                target.OwningConnection.SendGamePacket(packet);
                break;

            default:
                Logger.WriteLog(LogType.Error, $"Unhandled ChatType {packet.ChatType} in HandleChat!");
                break;
        }

        // TODO: later: handle chat commands and send the chat packet to the proper recipient(s)
    }

    public void HandleBroadcastPacket(TNLConnection connection, BinaryReader reader)
    {
        var broadcastPacket = new BroadcastPacket();
        broadcastPacket.Read(reader);

        if (broadcastPacket.Message.StartsWith('/'))
            return;

        connection.SendGamePacket(broadcastPacket);

        // TODO: later: handle chat commands and send the chat packet to the proper recipient(s)
    }
}
