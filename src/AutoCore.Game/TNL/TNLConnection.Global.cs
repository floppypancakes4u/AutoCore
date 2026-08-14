using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Linq;

namespace AutoCore.Game.TNL;

using AutoCore.Database.Char;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Global;
using AutoCore.Utils;

public partial class TNLConnection
{
    /// <summary>
    /// Address advertised to clients in the Global→Sector hand-off packet.
    /// Defaults to loopback for single-machine setups; GlobalServer.Setup overwrites
    /// this with <c>GameConfig.PublicAddress</c> so LAN/remote clients reach the host.
    /// </summary>
    public static IPAddress SectorRedirectAddress { get; set; } = IPAddress.Loopback;

    /// <summary>
    /// Builds the sector transfer packet from <see cref="SectorRedirectAddress"/>.
    /// Extracted so unit tests can pin the advertised IP/port without a live DB.
    /// </summary>
    internal static TransferToSectorPacket BuildSectorTransfer() => new()
    {
        IPAddress = SectorRedirectAddress,
        Port = 27001,
        Flags = 0
    };

    private void HandleNewsPacket(BinaryReader reader)
    {
        var packet = new NewsPacket();
        packet.Read(reader);

        const string news = "Welcome everybody to the world first [$emote]Auto Assault Private Server[$/emote]!\nHave fun, and enjoy your stay! :)";

        SendGamePacket(new NewsPacket(news, packet.Language));
    }

    /// <summary>
    /// Global-sector handoff: loads character via live <see cref="CharContext"/> and transfers
    /// to sector. Soft-fail Disconnect paths require live EF; Login/News/Disconnect handlers
    /// covered via TestPacketSink unit tests.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Live CharContext EF I/O for GetOrLoadCharacter; Global news/disconnect unit-tested.")]
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

        // IMPORTANT:
        // ConvoyMissionsRequest (0x800F) is a Global opcode and arrives on the Global connection.
        // The sector connection sets CurrentCharacter during TransferFromGlobal, but the Global
        // connection previously never did — causing "CurrentCharacter is null" even after the
        // player has been in-game for a while.
        CurrentCharacter = character;
        CurrentCharacter.SetOwningConnection(this);

        AutoCore.Utils.Logging.GameLog.Info("CharacterSelected",
            ("SessionId", SessionId),
            ("CharacterId", character.ObjectId.Coid),
            ("AccountId", character.AccountId));

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
        // Today the sector host shares Global's public address (same process / same machine).
        SendGamePacket(BuildSectorTransfer());
    }

    public void HandleDisconnectPacket(BinaryReader reader)
    {
        var packet = new DisconnectPacket();
        packet.Read(reader);

        SendGamePacket(new DisconnectAckPacket());
    }

    private void HandleConvoyMissionsRequest(BinaryReader reader)
    {
        // Retail request is opcode-only. Drain leftover bytes so a padded client frame stays framed.
        var remaining = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
        if (remaining > 0)
            _ = reader.ReadBytes(remaining);

        if (CurrentCharacter == null)
        {
            Logger.WriteLog(LogType.Error, "HandleConvoyMissionsRequest: CurrentCharacter is null");
            return;
        }

        SendGamePacket(new ConvoyMissionsResponsePacket
        {
            CoidMember = CurrentCharacter.ObjectId.Coid,
            MissionIds = CurrentCharacter.CurrentQuests.Select(q => q.MissionId).ToList()
        });
    }
}
