using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Entities
{
    using CloneBases;
    using Database.Char;
    using Database.Char.Models;
    using Packets.Sector;

    public class WheelSet : SimpleObject
    {
        #region Properties
        #region Database WheelSet properties
        private SimpleObjectData DBData { get; set; }
        #endregion

        public CloneBaseWheelSet CloneBaseWheelSet => CloneBaseObject as CloneBaseWheelSet;
        #endregion

        public WheelSet()
        {
        }

        public override bool LoadFromDB(CharContext context, long coid)
        {
            SetCoid(coid, true);

            DBData = context.SimpleObjects.FirstOrDefault(so => so.Coid == coid);
            if (DBData == null)
                return false;

            LoadCloneBase(DBData.CBID);

            return true;
        }

        public override void WriteToPacket(CreateSimpleObjectPacket packet)
        {
            base.WriteToPacket(packet);

            if (packet is CreateWheelSetPacket wheelSetPacket)
            {
                wheelSetPacket.FrictionGravel = CloneBaseWheelSet.WheelSetSpecific.Friction[0];
                wheelSetPacket.FrictionIce = CloneBaseWheelSet.WheelSetSpecific.Friction[1];
                wheelSetPacket.FrictionMud = CloneBaseWheelSet.WheelSetSpecific.Friction[2];
                wheelSetPacket.FrictionPaved = CloneBaseWheelSet.WheelSetSpecific.Friction[3];
                wheelSetPacket.FrictionPlains = CloneBaseWheelSet.WheelSetSpecific.Friction[4];
                wheelSetPacket.FrictionSand = CloneBaseWheelSet.WheelSetSpecific.Friction[5];
                wheelSetPacket.IsDefault = false;
                wheelSetPacket.Name = "";
            }
        }
    }
}
