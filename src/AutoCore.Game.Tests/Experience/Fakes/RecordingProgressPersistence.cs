using AutoCore.Game.Experience;

namespace AutoCore.Game.Tests.Experience.Fakes;

public sealed class RecordingProgressPersistence : ICharacterProgressPersistence
{
    public Dictionary<long, CharacterProgressSnapshot> Store { get; } = new();
    public List<(long Coid, CharacterProgressSnapshot Progress)> Saves { get; } = new();
    public bool ThrowOnSave { get; set; }
    public bool ThrowOnLoad { get; set; }

    public CharacterProgressSnapshot LoadProgress(long characterCoid)
    {
        if (ThrowOnLoad)
            throw new InvalidOperationException($"LoadProgress forced fail for {characterCoid}");

        return Store.TryGetValue(characterCoid, out var snap)
            ? snap
            : new CharacterProgressSnapshot(1, 0);
    }

    public void SaveProgress(long characterCoid, CharacterProgressSnapshot progress)
    {
        if (ThrowOnSave)
            throw new InvalidOperationException($"SaveProgress forced fail for {characterCoid}");

        Store[characterCoid] = progress;
        Saves.Add((characterCoid, progress));
    }
}
