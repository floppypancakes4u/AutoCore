namespace AutoCore.Game.Skills;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;

/// <summary>Authoritative learned-skill and quick-bar storage.</summary>
public sealed class CharacterSkillPersistence
{
    public static CharacterSkillPersistence Instance { get; } = new();

    public void Save(long characterCoid, IReadOnlyDictionary<int, byte> skills, long[] items, int[] quickBarSkills)
    {
        using var context = new CharContext();
        var oldSkills = context.CharacterLearnedSkills.Where(x => x.CharacterCoid == characterCoid);
        context.CharacterLearnedSkills.RemoveRange(oldSkills);
        context.CharacterLearnedSkills.AddRange(skills.Select(x => new CharacterLearnedSkillData { CharacterCoid = characterCoid, SkillId = x.Key, Rank = x.Value }));
        var oldSlots = context.CharacterQuickBarSlots.Where(x => x.CharacterCoid == characterCoid);
        context.CharacterQuickBarSlots.RemoveRange(oldSlots);
        context.CharacterQuickBarSlots.AddRange(Enumerable.Range(0, 100).Where(i => items[i] != -1 || quickBarSkills[i] != 0)
            .Select(i => new CharacterQuickBarSlotData { CharacterCoid = characterCoid, Slot = (byte)i, ItemCoid = items[i], SkillId = quickBarSkills[i] }));
        context.SaveChanges();
    }
}
