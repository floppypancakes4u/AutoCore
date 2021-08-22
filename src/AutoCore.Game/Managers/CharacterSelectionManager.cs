using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Managers
{
    using Constants;
    using Database.Char;
    using Database.Char.Models;
    using Database.World;
    using Database.World.Models;
    using Packets.Login;
    using TNL;
    using Utils.Memory;

    public class CharacterSelectionManager : Singleton<CharacterSelectionManager>
    {
        public (bool, long) CreateNewCharacter(TNLConnection client, NewCharacterPacket packet)
        {
            using var context = new CharContext();

            var existingCharacter = context.Characters.FirstOrDefault(c => c.Name == packet.CharacterName);
            if (existingCharacter != null)
                return (false, -1);

            var existingVehicle = context.CharacterVehicles.FirstOrDefault(v => v.Name == packet.CharacterName);
            if (existingVehicle != null)
                return (false, -1);

            // TODO: create the actual character, add every item to it and save it into the DB
            ConfigNewCharacter config;

            using (var worldContext = new WorldContext())
            {
                config = worldContext.ConfigNewCharacters.First(cnc => cnc.Race == 0 && cnc.Class == 3);
            }

            var charObj = new ClonebaseObject
            {
                Coid = 0,
                Type = (byte)ClonebaseObjectType.Character
            };

            var vehObj = new ClonebaseObject
            {
                Coid = 0,
                Type = (byte)ClonebaseObjectType.Vehicle
            };

            context.ClonebaseObjects.Add(charObj);
            context.ClonebaseObjects.Add(vehObj);
            context.SaveChanges();

            var character = new Character
            {
                Coid = charObj.Coid,
                AccountId = client.Account.Id,
                CBID = packet.CBID,
                Name = packet.CharacterName,
                HeadId = packet.HeadId,
                BodyId = packet.BodyId,
                HeadDetail1 = packet.HeadDetail1,
                HeadDetail2 = packet.HeadDetail2,
                HelmetId = packet.HelmetId,
                EyesId = packet.EyesId,
                MouthId = packet.MouthId,
                HairId = packet.HairId,
                PrimaryColor = packet.PrimaryColor,
                SecondaryColor = packet.SecondaryColor,
                EyesColor = packet.EyesColor,
                HairColor = packet.HairColor,
                SkinColor = packet.SkinColor,
                SpecialityColor = packet.SpecialityColor,
                ScaleOffset = packet.ScaleOffset
            };
            context.Characters.Add(character);

            var vehicle = new CharacterVehicle
            {
                Coid = vehObj.Coid,
                CharacterCoid = charObj.Coid,
                CBID = config.Vehicle
            };
            context.CharacterVehicles.Add(vehicle);
            context.SaveChanges();

            return (true, -1);
        }

        public void SendCharacterList(TNLConnection client)
        {
            /*var list = DataAccess.Character.GetCharacters(AccountId);

            foreach (var character in (from charData in list let character = new Character() where character.LoadFromDB(charData.Value, charData.Key) select character))
            {
                character.SetOwner(this);

                var pack = new Packet(Opcode.CreateCharacter);
                character.WriteToCreatePacket(pack);

                var vpack = new Packet(Opcode.CreateVehicle);
                character.GetVehicle().WriteToCreatePacket(vpack);

                client.SendGamePacket(pack);
                client.SendPacket(vpack);
            }*/
        }
    }
}
