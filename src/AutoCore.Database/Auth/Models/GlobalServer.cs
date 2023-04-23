using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Auth.Models;

[Table("global_server")]
public class GlobalServer
{
    [Key]
    public byte Id { get; set; }
    public string Password { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}