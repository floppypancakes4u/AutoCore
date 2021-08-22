using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models
{
    [Table("account")]
    public class Account
    {
        [Key]
        public uint Id { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public uint FirstFlags1 { get; set; }
        public uint FirstFlags2 { get; set; }
        public uint FirstFlags3 { get; set; }
        public uint FirstFlags4 { get; set; }
    }
}
