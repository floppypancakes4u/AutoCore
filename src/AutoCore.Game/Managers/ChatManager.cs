namespace AutoCore.Game.Managers;

using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils.Memory;

public class ChatManager : Singleton<ChatManager>
{
    public void HandleChat(TNLConnection connection, BinaryReader reader)
    {
        var chatPacket = new ChatPacket();
        chatPacket.Read(reader);

        if (chatPacket.Message.StartsWith('/'))
            return;

        connection.SendGamePacket(chatPacket);

        // TODO: later: handle chat commands and send the chat packet to the proper recipient(s)
    }

    public void HandleBroadcast(TNLConnection connection, BinaryReader reader)
    {
        var broadcastPacket = new BroadcastPacket();
        broadcastPacket.Read(reader);

        if (broadcastPacket.Message.StartsWith('/'))
            return;

        connection.SendGamePacket(broadcastPacket);

        // TODO: later: handle chat commands and send the chat packet to the proper recipient(s)
    }
}
