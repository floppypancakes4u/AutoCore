using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.World.Models;

[Table("continent_area")]
public class ContinentArea
{
    public int ContinentObjectId { get; set; }
    public byte Area { get; set; }
    public int XPLevel { get; set; }
    public string AreaName { get; set; }
}
