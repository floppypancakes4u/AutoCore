namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures.Social;
using AutoCore.Utils.Extensions;

public class GetEnemiesResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.GetEnemiesResponse;

    public List<Enemy> Enemies { get; set; } = [];

    public override void Write(BinaryWriter writer)
    {
        if (Enemies.Count > 20)
            throw new InvalidOperationException("There are too many enemies in the packet!");

        writer.Write(Enemies.Count);

        foreach (var enemy in Enemies)
        {
            writer.Write(enemy.CoidCharacter);
            writer.Write(enemy.CoidEnemyCharacter);
            writer.Write(enemy.Level);
            writer.Write(enemy.LastContinentId);
            writer.Write(enemy.TimesKilled);
            writer.Write(enemy.TimesKilledBy);
            writer.Write(enemy.Race);
            writer.Write(enemy.Class);
            writer.Write(enemy.Online);
            writer.WriteUtf8StringOn(enemy.Name, 17);

            writer.BaseStream.Position += 4;
        }
    }
}
