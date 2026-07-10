namespace AutoCore.Game.Managers;

using System.Diagnostics.CodeAnalysis;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// EF-backed world-state persistence. Opens a short-lived <see cref="CharContext"/> per call.
/// Core apply/lookup logic is store-abstracted for unit tests without a live database.
/// </summary>
public sealed class CharacterWorldStatePersistence : ICharacterWorldStatePersistence
{
    public static CharacterWorldStatePersistence Instance { get; } = new();

    /// <summary>
    /// When set (unit tests), <see cref="Save"/> uses this store instead of opening MySQL.
    /// </summary>
    internal Func<IWorldStateStore> StoreFactoryForTests { get; set; }

    /// <summary>
    /// Production DB open path. Overridable in tests so the branch is exercised without MySQL.
    /// </summary>
    internal Action<CharacterWorldStateSnapshot> ProductionSave { get; set; } = SaveWithProductionStore;

    /// <summary>
    /// Capture live map/pose into entity DBData and flush to the char database.
    /// Safe no-op when the character has no attached DB row.
    /// </summary>
    public static void PersistFromCharacter(Character character, ICharacterWorldStatePersistence persistence = null)
    {
        if (character == null)
            return;

        persistence ??= Instance;

        var snapshot = character.CaptureWorldStateToDb();
        if (snapshot == null)
            return;

        persistence.Save(snapshot.Value);
    }

    public void Save(CharacterWorldStateSnapshot snapshot)
    {
        if (snapshot.CharacterCoid <= 0)
            return;

        try
        {
            if (StoreFactoryForTests != null)
            {
                SaveWithStore(StoreFactoryForTests(), snapshot);
                return;
            }

            ProductionSave(snapshot);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                $"CharacterWorldStatePersistence.Save failed for character {snapshot.CharacterCoid}: {ex.Message}");
            throw;
        }
    }

    /// <summary>Opens a live CharContext (MySQL). Not unit-tested; use <see cref="SaveWithStore"/>.</summary>
    [ExcludeFromCodeCoverage]
    private static void SaveWithProductionStore(CharacterWorldStateSnapshot snapshot)
    {
        using var context = new CharContext();
        SaveWithStore(new CharContextWorldStateStore(context), snapshot);
    }

    /// <summary>
    /// Persist via an abstract store (production uses EF; tests use an in-memory fake).
    /// </summary>
    internal static void SaveWithStore(IWorldStateStore store, CharacterWorldStateSnapshot snapshot)
    {
        if (store == null)
            throw new ArgumentNullException(nameof(store));

        if (snapshot.CharacterCoid <= 0)
            return;

        var character = store.FindCharacter(snapshot.CharacterCoid);
        if (character == null)
        {
            Logger.WriteLog(LogType.Error,
                $"CharacterWorldStatePersistence.Save: character {snapshot.CharacterCoid} not found");
            return;
        }

        ApplyToCharacter(character, snapshot);

        if (snapshot.VehicleCoid > 0)
        {
            var vehicle = store.FindVehicle(snapshot.VehicleCoid);
            if (vehicle != null)
            {
                ApplyToVehicle(vehicle, snapshot);
            }
            else
            {
                Logger.WriteLog(LogType.Error,
                    $"CharacterWorldStatePersistence.Save: vehicle {snapshot.VehicleCoid} not found");
            }
        }

        store.SaveChanges();
        Logger.WriteLog(LogType.Debug,
            $"CharacterWorldStatePersistence.Save: character={snapshot.CharacterCoid} continent={snapshot.ContinentId} " +
            $"pos=({snapshot.PositionX},{snapshot.PositionY},{snapshot.PositionZ})");
    }

    internal static void ApplyToCharacter(CharacterData character, CharacterWorldStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(character);

        character.LastTownId = snapshot.ContinentId;
        character.PositionX = snapshot.PositionX;
        character.PositionY = snapshot.PositionY;
        character.PositionZ = snapshot.PositionZ;
        character.RotationX = snapshot.RotationX;
        character.RotationY = snapshot.RotationY;
        character.RotationZ = snapshot.RotationZ;
        character.RotationW = snapshot.RotationW;
    }

    internal static void ApplyToVehicle(VehicleData vehicle, CharacterWorldStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        vehicle.PositionX = snapshot.PositionX;
        vehicle.PositionY = snapshot.PositionY;
        vehicle.PositionZ = snapshot.PositionZ;
        vehicle.RotationX = snapshot.RotationX;
        vehicle.RotationY = snapshot.RotationY;
        vehicle.RotationZ = snapshot.RotationZ;
        vehicle.RotationW = snapshot.RotationW;
    }

    /// <summary>Abstract store used by <see cref="SaveWithStore"/>.</summary>
    internal interface IWorldStateStore
    {
        CharacterData FindCharacter(long coid);
        VehicleData FindVehicle(long coid);
        void SaveChanges();
    }

    /// <summary>
    /// Thin EF adapter. Requires a live Char DB; production-only path.
    /// Business logic is covered via <see cref="SaveWithStore"/> + fakes.
    /// </summary>
    [ExcludeFromCodeCoverage]
    private sealed class CharContextWorldStateStore : IWorldStateStore
    {
        private readonly CharContext _context;

        public CharContextWorldStateStore(CharContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public CharacterData FindCharacter(long coid) =>
            _context.Characters.FirstOrDefault(c => c.Coid == coid);

        public VehicleData FindVehicle(long coid) =>
            _context.Vehicles.FirstOrDefault(v => v.Coid == coid);

        public void SaveChanges() => _context.SaveChanges();
    }
}
