using AutoCore.Game.Packets.Sector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Entities
{
    public class SimpleObject : ClonedObjectBase
    {
        protected int[] Prefixes { get; set; }
        protected int[] Gadgets { get; set; }
        protected short MaxGadgets { get; set; }
        protected int TeamFaction { get; set; }
        protected int Quantity { get; set; }
        protected uint HP { get; set; }
        protected uint MaxHP { get; set; }
        protected int ItemTemplateId { get; set; }
        protected byte InventoryPositionX { get; set; }
        protected byte InventoryPositionY { get; set; }
        protected byte SkillLevel1 { get; set; }
        protected byte SkillLevel2 { get; set; }
        protected byte SkillLevel3 { get; set; }
        protected bool AlreadyAssembled { get; set; }

        public SimpleObject()
            : base()
        {
            MaxGadgets = 0;
            TeamFaction = 0;
            HP = 0;
            MaxHP = 500;
            InventoryPositionX = 0;
            InventoryPositionY = 0;
            AlreadyAssembled = false;
            Quantity = 1;
            ItemTemplateId = -1;
            SkillLevel1 = 1;
            SkillLevel2 = 1;
            SkillLevel3 = 1;
        }

        public override void WriteToPacket(CreateSimpleObjectPacket packet)
        {
            base.WriteToPacket(packet);

            packet.MaxGadgets = MaxGadgets;
            packet.TeamFaction = TeamFaction;
            packet.InventoryPositionX = InventoryPositionX;
            packet.InventoryPositionY = InventoryPositionY;
            packet.Quantity = Quantity;
            packet.ItemTemplateId = ItemTemplateId;
            packet.SkillLevel1 = SkillLevel1;
            packet.SkillLevel2 = SkillLevel2;
            packet.SkillLevel3 = SkillLevel3;
        }
    }
}
