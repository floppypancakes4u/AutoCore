using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.World.Models;

[Table("config_new_character")]
public class ConfigNewCharacter
{
    public byte Race { get; set; }
    public byte Class { get; set; }
    public int OptionCode { get; set; }
    public int PowerPlant { get; set; }
    public int Armor { get; set; }
    public int RaceItem { get; set; }
    public uint SkillBattleMode1 { get; set; }
    public uint SkillBattleMode2 { get; set; }
    public uint SkillBattleMode3 { get; set; }
    public uint StartSkill { get; set; }
    public int StartTown { get; set; }
    public int Trailer { get; set; }
    public int Vehicle { get; set; }
    public int Weapon { get; set; }
}
