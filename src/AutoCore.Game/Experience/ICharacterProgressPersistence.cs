namespace AutoCore.Game.Experience;

/// <summary>
/// Absolute load/save for character Level + Experience (+ level-up pools).
/// Mirrors credits-style write-through (docs/XP.md).
/// </summary>
public interface ICharacterProgressPersistence
{
    CharacterProgressSnapshot LoadProgress(long characterCoid);

    /// <summary>Overwrite absolute progress. Throws if the character row is missing.</summary>
    void SaveProgress(long characterCoid, CharacterProgressSnapshot progress);
}
