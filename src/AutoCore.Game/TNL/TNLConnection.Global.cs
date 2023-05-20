using System.Net;

namespace AutoCore.Game.TNL;

using AutoCore.Database.Char;
using AutoCore.Game.CloneBases;
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

        // New character, that never entered the world
        if (character.LastTownId == -1)
        {
            var characterCloneBase = AssetManager.Instance.GetCloneBase<CloneBaseCharacter>(character.CBID);
            if (characterCloneBase == null)
            {
                Disconnect("Invalid character");

                return;
            }

            var configNewCharacter = AssetManager.Instance.GetConfigNewCharacterFor(characterCloneBase.CharacterSpecific.Race, characterCloneBase.CharacterSpecific.Class);
            if (configNewCharacter == null)
            {
                Disconnect("Invalid character");

                return;
            }

            var map = MapManager.Instance.GetMap(configNewCharacter.StartTown);
            if (map == null)
            {
                Disconnect("Invalid character");

                return;
            }

            character.EnterMap(map);

            // HACK, the character should be loaded in the above context, so save it back.
            // even if the character is loaded form cache/reloaded from DB later, it should have a townid now, so this code isn't triggered
            // otherwise the context would already be detached from the DBData inside Character, which would be bad/not working
            context.SaveChanges();
        }

        // TODO: select sector server based on registered sector servers
        // default to localhost, for now

        SendGamePacket(new TransferToSectorPacket()
        {
            IPAddress = IPAddress.Loopback,
            Port = 27001,
            Flags = 0
        });
    }
}
