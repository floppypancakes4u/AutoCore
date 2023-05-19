using System.Net;

namespace AutoCore.Game.TNL;

using AutoCore.Database.Char;
using AutoCore.Game.Managers;
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

        using var context = new CharContext();

        var character = ObjectManager.Instance.GetOrLoadCharacter(packet.CharacterCoid, context);

        SendGamePacket(new LoginAckPacket
        {
            Success = character != null
        });

        if (character == null)
            return;

        SendGamePacket(new TransferToSectorPacket()
        {
            IPAddress = IPAddress.Loopback,
            Port = 27001,
            Flags = 0
        });
    }
}
