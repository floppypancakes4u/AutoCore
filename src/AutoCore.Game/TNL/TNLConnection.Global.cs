using System.Net;

namespace AutoCore.Game.TNL;

using AutoCore.Game.Packets.Global;

public partial class TNLConnection
{
    private void HandleNews(BinaryReader reader)
    {
        var packet = new NewsPacket();
        packet.Read(reader);

        const string news = "Welcome everybody to the world first [$emote]Auto Assault Private Server[$/emote]!\nHave fun, and enjoy your stay! :)";

        SendGamePacket(new NewsPacket(news, packet.Language));
    }

    private void HandleLoginPacket(BinaryReader reader)
    {
        var packet = new LoginPacket();
        packet.Read(reader);

        // TODO: check character

        SendGamePacket(new LoginAckPacket
        {
            Success = true
        });

        SendGamePacket(new TransferToSectorPacket()
        {
            IPAddress = IPAddress.Loopback,
            Port = 27001,
            Flags = 0
        });
    }
}
