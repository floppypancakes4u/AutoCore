using AutoCore.Game.Packets.Sector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Entities
{
    public class Character : Creature
    {
        public byte GMLevel { get; }
        public string Name { get; private set; }
        public string ClanName { get; private set; }
        public int ClanId { get; private set; }
        public int ClanRank { get; private set; }
        public int BodyId { get; private set; }
        public int HeadId { get; private set; }
        public int HairId { get; private set; }
        public int HelmetId { get; private set; }
        public int AccessoryId1 { get; private set; }
        public int AccessoryId2 { get; private set; }
        public int EyesId { get; private set; }
        public int MouthId { get; private set; }
        public float ScaleOffset { get; private set; }

        public Character()
        {
            ScaleOffset = 0.0f;
            Name = "";
            ClanName = "";
            GMLevel = 0;
            BodyId = -1;
            HeadId = -1;
            HairId = -1;
            HelmetId = -1;
            AccessoryId1 = -1;
            AccessoryId2 = -1;
            EyesId = -1;
            MouthId = -1;
            ClanId = -1;
            ClanRank = -1;
        }

        public override void WriteToPacket(CreateSimpleObjectPacket packet)
        {
            base.WriteToPacket(packet);

            if (packet is CreateCharacterPacket charPacket)
            {
                charPacket.CurrentVehicleCoid = -1;
                charPacket.CurrentTrailerCoid = -1;
                charPacket.HeadId = HeadId;
                // TODO
            }

            if (packet is CreateCharacterExtendedPacket extendedCharPacket)
            {
                // TODO
            }
        }
    }
}
