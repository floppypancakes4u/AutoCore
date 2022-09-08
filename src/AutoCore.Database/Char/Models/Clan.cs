using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("clan")]
public class Clan
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; }
    public string MOTD { get; set; }
    public string Rank1 { get; set; }
    public string Rank2 { get; set; }
    public string Rank3 { get; set; }
    public int MonthlyDues { get; set; }
    public int MonthlyUpkeep { get; set; }

    [InverseProperty("Clan")]
    public List<ClanMember> Members { get; set; }

    public Clan()
    {
        Id = -1;
        Name = "";
    }
}
