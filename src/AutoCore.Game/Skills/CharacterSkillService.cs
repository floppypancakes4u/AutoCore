namespace AutoCore.Game.Skills;

using AutoCore.Game.Entities;
using AutoCore.Game.Experience;
using AutoCore.Game.Managers;

public sealed class CharacterSkillService
{
    public static CharacterSkillService Instance { get; } = new();

    /// <summary>Test hook: when set, replaces DB persistence so unit tests need no CharContext.</summary>
    internal static Action<Character> PersistForTests { get; set; }

    public bool TryIncrement(Character character, int skillId, out string error)
    {
        error = null;
        var skill = AssetManager.Instance.GetSkill(skillId);
        if (character == null || skill == null) { error = "Unknown skill."; return false; }
        if (character.SkillPoints <= 0) { error = "No skill points available."; return false; }
        if (character.Level < skill.MinimumLevel) { error = "Character level is too low."; return false; }
        var rank = character.LearnedSkills.GetValueOrDefault(skillId);
        if (rank >= skill.MaxSkillLevel) { error = "Skill is already at maximum rank."; return false; }
        foreach (var prerequisite in new[] { skill.SkillPrerequisite1, skill.SkillPrerequisite2, skill.SkillPrerequisite3 }.Where(x => x > 0))
            if (!character.LearnedSkills.ContainsKey(prerequisite)) { error = "Skill prerequisite is not learned."; return false; }
        character.LearnedSkills[skillId] = (byte)(rank + 1);
        character.SetSkillPoints((short)(character.SkillPoints - 1));
        Persist(character);
        return true;
    }

    public bool TryUpdateQuickBar(Character character, int slot, long itemCoid, int skillId, out string error)
    {
        error = null;
        if (character == null || slot is < 0 or >= 100) { error = "Invalid quick-bar slot."; return false; }
        // Client may send skillId=-1 when clearing; treat any non-positive as empty.
        if (skillId < 0)
            skillId = 0;
        if (skillId != 0 && !character.LearnedSkills.ContainsKey(skillId)) { error = "Skill is not learned."; return false; }
        if (itemCoid != -1 && itemCoid != 0 && !character.Inventory.Items.Any(x => x.Coid == itemCoid)) { error = "Item is not in cargo."; return false; }
        character.QuickBarItemCoids[slot] = itemCoid == 0 ? -1 : itemCoid;
        character.QuickBarSkills[slot] = skillId;
        Persist(character);
        return true;
    }

    public void Reset(Character character)
    {
        character.LearnedSkills.Clear();
        Array.Clear(character.QuickBarSkills);
        Persist(character);
    }

    public void SetPoints(Character character, short points) { character.SetSkillPoints(points); Persist(character); }

    private static void Persist(Character character)
    {
        if (PersistForTests != null)
        {
            PersistForTests(character);
            return;
        }

        CharacterProgressPersistence.Instance.SaveProgress(character.ObjectId.Coid, character.ToProgressSnapshot());
        CharacterSkillPersistence.Instance.Save(character.ObjectId.Coid, character.LearnedSkills, character.QuickBarItemCoids, character.QuickBarSkills);
    }
}
