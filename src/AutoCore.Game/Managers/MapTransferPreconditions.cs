namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;

/// <summary>
/// Preconditions for <see cref="MapManager.TransferCharacterToMap"/>.
/// Kept pure so transfer safety can be regression-tested without map/network I/O.
/// </summary>
public static class MapTransferPreconditions
{
    public enum Failure
    {
        None = 0,
        CharacterNull,
        NoConnection,
        NoVehicle
    }

    public static Failure Validate(Character character)
    {
        if (character is null)
            return Failure.CharacterNull;

        if (character.OwningConnection is null)
            return Failure.NoConnection;

        if (character.CurrentVehicle is null)
            return Failure.NoVehicle;

        return Failure.None;
    }

    public static bool TryValidate(Character character, out Failure failure)
    {
        failure = Validate(character);
        return failure == Failure.None;
    }

    public static string Describe(Failure failure) => failure switch
    {
        Failure.None => null,
        Failure.CharacterNull => "TransferCharacterToMap: character is null!",
        Failure.NoConnection => "TransferCharacterToMap: character has no connection!",
        Failure.NoVehicle => "TransferCharacterToMap: character has no vehicle!",
        _ => $"TransferCharacterToMap: unknown precondition failure ({failure})!"
    };
}
