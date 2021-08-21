using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Managers
{
    using TNL;
    using Utils.Memory;

    public class CharacterSelectionManager : Singleton<CharacterSelectionManager>
    {
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
