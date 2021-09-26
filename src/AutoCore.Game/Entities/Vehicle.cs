using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Entities
{
    using Database.Char;
    using Packets.Sector;

    public class Vehicle : SimpleObject
    {
        public bool LoadFromDB(CharContext context, long coid)
        {
            return true;
        }

        public override void WriteToPacket(CreateSimpleObjectPacket packet)
        {
            base.WriteToPacket(packet);

            if (packet is CreateVehiclePacket vehiclePacket)
            {
            }

            if (packet is CreateVehicleExtendedPacket extendedPacket)
            {
            }
        }
    }
}
