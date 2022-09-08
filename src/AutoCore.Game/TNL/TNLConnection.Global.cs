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
}
