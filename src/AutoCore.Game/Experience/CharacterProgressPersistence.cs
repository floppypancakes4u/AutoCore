namespace AutoCore.Game.Experience;

using AutoCore.Database.Char;
using AutoCore.Utils;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF-backed progress persistence. Opens a short-lived <see cref="CharContext"/> per call.
/// </summary>
public sealed class CharacterProgressPersistence : ICharacterProgressPersistence
{
    public static CharacterProgressPersistence Instance { get; } = new();

    /// <summary>Factory for short-lived contexts (tests inject InMemory / fake contexts).</summary>
    internal Func<CharContext> CreateContext { get; set; } = static () => new CharContext();

    /// <summary>Restore default factory after tests.</summary>
    internal void ResetForTests() => CreateContext = static () => new CharContext();

    public CharacterProgressSnapshot LoadProgress(long characterCoid)
    {
        using var context = CreateContext();
        var character = context.Characters.AsNoTracking().FirstOrDefault(c => c.Coid == characterCoid);
        if (character == null)
            return new CharacterProgressSnapshot(1, 0);

        return new CharacterProgressSnapshot(
            character.Level,
            character.Experience,
            character.SkillPoints,
            character.AttributePoints,
            character.ResearchPoints,
            character.AttributeTech,
            character.AttributeCombat,
            character.AttributeTheory,
            character.AttributePerception);
    }

    public void SaveProgress(long characterCoid, CharacterProgressSnapshot progress)
    {
        using var context = CreateContext();
        var character = context.Characters.FirstOrDefault(c => c.Coid == characterCoid);
        if (character == null)
        {
            throw new InvalidOperationException(
                $"SaveProgress: character {characterCoid} not found; progress not saved");
        }

        character.Level = progress.Level;
        character.Experience = progress.Experience;
        character.SkillPoints = progress.SkillPoints;
        character.AttributePoints = progress.AttributePoints;
        character.ResearchPoints = progress.ResearchPoints;
        character.AttributeTech = progress.AttributeTech;
        character.AttributeCombat = progress.AttributeCombat;
        character.AttributeTheory = progress.AttributeTheory;
        character.AttributePerception = progress.AttributePerception;
        context.SaveChanges();

        Logger.WriteLog(
            LogType.Network,
            $"SaveProgress: character={characterCoid} level={progress.Level} xp={progress.Experience} " +
            $"skill={progress.SkillPoints} attrib={progress.AttributePoints} research={progress.ResearchPoints} " +
            $"tech={progress.AttributeTech} combat={progress.AttributeCombat} theory={progress.AttributeTheory} perception={progress.AttributePerception}");
    }
}
