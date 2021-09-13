using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Entities
{
    using Database.Char.Models;
    using Packets.Sector;

    using CharacterData = Database.Char.Models.Character;

    public class Character : Creature
    {
        #region Properties
        #region Database Character Data
        public CharacterData CharacterDBData { get; private set; }
        public string Name => CharacterDBData.Name;
        public int BodyId => CharacterDBData.BodyId;
        public int HeadId => CharacterDBData.HeadId;
        public int HairId => CharacterDBData.HairId;
        public int HelmetId => CharacterDBData.HelmetId;
        public int AccessoryId1 => CharacterDBData.HeadDetail1;
        public int AccessoryId2 => CharacterDBData.HeadDetail2;
        public int EyesId => CharacterDBData.EyesId;
        public int MouthId => CharacterDBData.MouthId;
        public float ScaleOffset => CharacterDBData.ScaleOffset;
        #endregion

        #region Database Clan Data
        public ClanMember ClanMemberDBData { get; private set; }
        public string ClanName => ClanMemberDBData?.Clan?.Name;
        public int ClanId => ClanMemberDBData?.ClanId ?? -1;
        public int ClanRank => ClanMemberDBData?.Rank ?? -1;
        #endregion

        public byte GMLevel { get; }
        #endregion

        public Character()
        {
            CharacterDBData = new CharacterData();
            
            GMLevel = 0;
        }

        public bool LoadFromDB(long coid)
        {
            // TODO: load character data
            // TODO: load clan data
            return false;
        }

        public override void WriteToPacket(CreateSimpleObjectPacket packet)
        {
            base.WriteToPacket(packet);

            if (packet is CreateCharacterPacket charPacket)
            {
                charPacket.CurrentVehicleCoid = -1; // TODO
                charPacket.CurrentTrailerCoid = -1; // TODO
                charPacket.HeadId = CharacterDBData.HeadId;
                charPacket.BodyId = CharacterDBData.BodyId;
                charPacket.AccessoryId1 = CharacterDBData.HeadDetail1;
                charPacket.AccessoryId2 = CharacterDBData.HeadDetail2;
                charPacket.HairId = CharacterDBData.HairId;
                charPacket.MouthId = CharacterDBData.MouthId;
                charPacket.EyesId = CharacterDBData.EyesId;
                charPacket.HelmetId = CharacterDBData.HelmetId;
                charPacket.PrimaryColor = CharacterDBData.PrimaryColor;
                charPacket.SecondaryColor = CharacterDBData.SecondaryColor;
                charPacket.EyesColor = CharacterDBData.EyesColor;
                charPacket.HairColor = CharacterDBData.HairColor;
                charPacket.SkinColor = CharacterDBData.SkinColor;
                charPacket.SpecialityColor = CharacterDBData.SpecialityColor;
                charPacket.LastTownId = 0; // TODO
                charPacket.LastStationMapId = 0; // TODO
                charPacket.Level = CharacterDBData.Level; // TODO
                charPacket.Bf297 = 0; // TODO
                charPacket.GMLevel = GMLevel;
                charPacket.ServerTime = 0; // TODO
                charPacket.Name = Name;
                charPacket.ClanName = ClanMemberDBData?.Clan?.Name ?? "";
                charPacket.CharacterScaleOffset = CharacterDBData.ScaleOffset;
            }

            if (packet is CreateCharacterExtendedPacket extendedCharPacket)
            {
                // TODO
            }
        }
    }
}
