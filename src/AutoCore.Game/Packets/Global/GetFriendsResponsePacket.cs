namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures.Social;
using AutoCore.Utils.Extensions;

public class GetFriendsResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.GetFriendsResponse;

    public List<Friend> Friends { get; set; } = [];

    public override void Write(BinaryWriter writer)
    {
        if (Friends.Count > 20)
            throw new InvalidOperationException("There are too many friends in the packet!");

        writer.Write(Friends.Count);

        foreach (var friend in Friends)
        {
            writer.Write(friend.CoidCharacter);
            writer.Write(friend.CoidFriendCharacter);
            writer.Write(friend.Level);
            writer.Write(friend.LastContinentId);
            writer.Write(friend.Class);
            writer.Write(friend.Online);
            writer.WriteUtf8StringOn(friend.Name, 17);

            writer.BaseStream.Position += 5;
        }
    }
}
