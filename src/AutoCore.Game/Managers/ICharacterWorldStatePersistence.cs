namespace AutoCore.Game.Managers;

using AutoCore.Game.Structures;

/// <summary>
/// Persists character continent + world pose (and active vehicle pose) to the char database.
/// </summary>
public interface ICharacterWorldStatePersistence
{
    void Save(CharacterWorldStateSnapshot snapshot);
}
