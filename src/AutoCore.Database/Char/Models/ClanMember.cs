using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models
{
    [Table("clan_member")]
    public class ClanMember
    {
        public int ClanId { get; set; }
        public long CharacterCoid { get; set; }
        public int Rank { get; set; }

        public Clan Clan { get; set; }

        public ClanMember()
        {
            ClanId = -1;
        }
    }
}
