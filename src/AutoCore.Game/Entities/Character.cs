using Microsoft.EntityFrameworkCore;

namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

public partial class Character : Creature
{
    #region Properties
    #region Database Character Data
    private CharacterData DBData { get; set; }
    public uint AccountId => DBData.AccountId;
    public string Name => DBData.Name;
    public long ActiveVehicleCoid => DBData.ActiveVehicleCoid ?? -1;
    public int BodyId => DBData.BodyId;
    public int HeadId => DBData.HeadId;
    public int HeadDetail1 => DBData.HeadDetail1;
    public int HeadDetail2 => DBData.HeadDetail2;
    public int HairId => DBData.HairId;
    public int HelmetId => DBData.HelmetId;
    public int AccessoryId1 => DBData.HeadDetail1;
    public int AccessoryId2 => DBData.HeadDetail2;
    public int EyesId => DBData.EyesId;
    public int MouthId => DBData.MouthId;
    public uint PrimaryColor => DBData.PrimaryColor;
    public uint SecondaryColor => DBData.SecondaryColor;
    public uint EyesColor => DBData.EyesColor;
    public uint HairColor => DBData.HairColor;
    public uint SkinColor => DBData.SkinColor;
    public uint SpecialityColor => DBData.SpecialityColor;
    public float ScaleOffset => DBData.ScaleOffset;
    public int LastTownId => DBData?.LastTownId ?? -1;
    public int LastStationMapId => DBData?.LastStationMapId ?? -1;
    public int LastStationId => DBData?.LastStationId ?? -1;
    public new byte Level => DBData?.Level ?? 1;

    /// <summary>Cumulative experience total (persisted). See docs/XP.md.</summary>
    public int Experience => DBData?.Experience ?? 0;

    /// <summary>Unspent skill points (persisted).</summary>
    public short SkillPoints => DBData?.SkillPoints ?? 0;

    /// <summary>Unspent attribute points (persisted).</summary>
    public short AttributePoints => DBData?.AttributePoints ?? 0;

    /// <summary>Unspent research points (persisted).</summary>
    public short ResearchPoints => DBData?.ResearchPoints ?? 0;

    /// <summary>Absolute money balance (persisted). Client UI splits into Globes/Bars/Scrip/Clink.</summary>
    public long Credits => DBData?.Credits ?? 0L;

    /// <summary>Optional server-side debt (not written on login spawn).</summary>
    public long CreditDebt => DBData?.CreditDebt ?? 0L;
    #endregion

    private int _runtimeLastStationId = -1;
    private int _runtimeLastStationMapId = -1;
    private bool _hasRuntimeLastStation;
    private bool _hasRuntimeLastStationPose;
    private Vector3 _runtimeLastStationPosition;
    private Quaternion _runtimeLastStationRotation = Quaternion.Default;

    /// <summary>
    /// Records the last repair station the player visited (MarkRepairStation reaction).
    /// Stores pad pose for respawn; also writes ids through to <see cref="DBData"/> when loaded.
    /// </summary>
    public void SetLastRepairStation(int stationId, int stationMapId, Vector3? position = null, Quaternion? rotation = null)
    {
        _runtimeLastStationId = stationId;
        _runtimeLastStationMapId = stationMapId;
        _hasRuntimeLastStation = true;

        if (position.HasValue)
        {
            _runtimeLastStationPosition = position.Value;
            _runtimeLastStationRotation = rotation ?? Quaternion.Default;
            _hasRuntimeLastStationPose = true;
        }

        if (DBData != null)
        {
            DBData.LastStationId = stationId;
            DBData.LastStationMapId = stationMapId;
        }
    }

    /// <summary>Effective last station id (runtime mark preferred over DB defaults).</summary>
    public int GetLastStationId() =>
        _hasRuntimeLastStation ? _runtimeLastStationId : (DBData?.LastStationId ?? -1);

    /// <summary>Effective last station map id (runtime mark preferred over DB defaults).</summary>
    public int GetLastStationMapId() =>
        _hasRuntimeLastStation ? _runtimeLastStationMapId : (DBData?.LastStationMapId ?? -1);

    /// <summary>True when MarkRepairStation stored a concrete pad pose this session.</summary>
    public bool TryGetLastStationPose(out Vector3 position, out Quaternion rotation)
    {
        if (_hasRuntimeLastStationPose)
        {
            position = _runtimeLastStationPosition;
            rotation = _runtimeLastStationRotation;
            return true;
        }

        position = default;
        rotation = Quaternion.Default;
        return false;
    }

    public override byte GetLevel() => Level;

    /// <summary>Update in-memory level (caller persists via CharacterProgressPersistence).</summary>
    public void SetLevel(byte level)
    {
        if (DBData == null)
            return;
        DBData.Level = level < 1 ? (byte)1 : level;
    }

    /// <summary>Update in-memory cumulative experience (caller persists).</summary>
    public void SetExperience(int experience)
    {
        if (DBData == null)
            return;
        DBData.Experience = experience < 0 ? 0 : experience;
    }

    public void SetSkillPoints(short points)
    {
        if (DBData == null)
            return;
        DBData.SkillPoints = points < 0 ? (short)0 : points;
    }

    public void SetAttributePoints(short points)
    {
        if (DBData == null)
            return;
        DBData.AttributePoints = points < 0 ? (short)0 : points;
    }

    public void SetResearchPoints(short points)
    {
        if (DBData == null)
            return;
        DBData.ResearchPoints = points < 0 ? (short)0 : points;
    }

    /// <summary>Update in-memory credits (caller persists via InventoryManager).</summary>
    public void SetCredits(long credits)
    {
        if (DBData == null)
            return;
        DBData.Credits = credits;
    }

    public void SetCreditDebt(long debt)
    {
        if (DBData == null)
            return;
        DBData.CreditDebt = debt;
    }

    #region Database Clan Data
    private ClanMember ClanMemberDBData { get; set; }
    public string ClanName => ClanMemberDBData?.Clan?.Name;
    public int ClanId => ClanMemberDBData?.ClanId ?? -1;
    public int ClanRank => ClanMemberDBData?.Rank ?? -1;
    #endregion

    public byte GMLevel { get; set; }
    public TNLConnection OwningConnection { get; private set; }
    public Vehicle CurrentVehicle { get; private set; }
    private InventoryManager _inventory = new(InventoryPersistence.Instance);
    public InventoryManager Inventory => _inventory;

    // Mission tracking
    public List<CharacterQuest> CurrentQuests { get; } = new();
    /// <summary>Finished mission ids (prereq checks for NPC offers).</summary>
    public HashSet<int> CompletedMissionIds { get; } = new();

    /// <summary>Per-map logic variables used by trigger/reaction conditions.</summary>
    public LogicVariableStore LogicVariables { get; private set; }

    /// <summary>
    /// Returns the logic-variable store for the character's current map, creating it if needed.
    /// </summary>
    public LogicVariableStore EnsureLogicVariables()
    {
        if (Map == null)
            return null;

        if (LogicVariables == null || LogicVariables.Map != Map)
            LogicVariables = new LogicVariableStore(Map, this);

        return LogicVariables;
    }
    #endregion

    public Character()
    {
        // Do not pre-seed starter missions. Client GiveMission rejects duplicates if the
        // create packet already carried the quest blob. Grant via map PerPlayerLoad → 0x206C.
    }

    public void SetOwningConnection(TNLConnection owningConnection)
    {
        OwningConnection = owningConnection;
    }

    /// <summary>
    /// Test/helper hook to attach a vehicle without loading from the character database.
    /// </summary>
    internal void SetCurrentVehicleForTests(Vehicle vehicle)
    {
        CurrentVehicle = vehicle;
        vehicle?.SetOwner(this);
    }

    /// <summary>Inventory-test alias for <see cref="SetCurrentVehicleForTests"/>.</summary>
    internal void AttachCurrentVehicleForTests(Vehicle vehicle) => SetCurrentVehicleForTests(vehicle);

    internal void AttachInventoryForTests(InventoryManager inventory)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    internal void AttachTestDataForTests(string name = "TestPilot")
    {
        DBData = new CharacterData
        {
            Coid = ObjectId.Coid,
            Name = name
        };
    }

    internal void SetLastTownIdForTests(int lastTownId)
    {
        if (DBData != null)
            DBData.LastTownId = lastTownId;
    }

    internal float GetDbPositionXForTests() => DBData?.PositionX ?? float.NaN;
    internal float GetDbPositionYForTests() => DBData?.PositionY ?? float.NaN;
    internal float GetDbPositionZForTests() => DBData?.PositionZ ?? float.NaN;
    internal float GetDbRotationXForTests() => DBData?.RotationX ?? float.NaN;
    internal float GetDbRotationWForTests() => DBData?.RotationW ?? float.NaN;

    public override Character GetAsCharacter() => this;
    public override Character GetSuperCharacter(bool includeSummons) => this;

    public override bool LoadFromDB(CharContext context, long coid, bool isInCharacterSelection = false)
    {
        SetCoid(coid, true);

        DBData = context.Characters.Include(c => c.SimpleObjectBase).FirstOrDefault(c => c.Coid == coid);
        if (DBData == null)
            return false;

        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"Character.LoadFromDB: Loaded character Coid {coid}, Name from DB: '{DBData.Name}' (Length: {DBData.Name?.Length ?? 0})");

        LoadCloneBase(DBData.SimpleObjectBase.CBID);

        ClanMemberDBData = context.ClanMembers.Include(cm => cm.Clan).FirstOrDefault(cm => cm.CharacterCoid == coid);

        Position = new(DBData.PositionX, DBData.PositionY, DBData.PositionZ);
        Rotation = new(DBData.RotationX, DBData.RotationY, DBData.RotationZ, DBData.RotationW);

        HP = MaxHP = CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
        Faction = CloneBaseObject.SimpleObjectSpecific.Faction;
        TeamFaction = CloneBaseObject.SimpleObjectSpecific.Faction;

        LoadExplorations(context);

        var width = DBData.CargoWidth > 0 ? DBData.CargoWidth : InventoryManager.DefaultCargoWidth;
        var pageCount = DBData.CargoPageCount > 0 ? DBData.CargoPageCount : InventoryManager.DefaultCargoPageCount;
        Inventory.SetCapacity(width, pageCount);

        if (!isInCharacterSelection)
        {
            var cargoItems = InventoryPersistence.Instance.LoadCargo(coid);
            Inventory.LoadItems(cargoItems);

            LoadMissions(context);
        }

        // TODO: set up stuff, fields, baseclasses, etc

        return true;
    }

    public bool LoadCurrentVehicle(CharContext context, bool isInCharacterSelection = false)
    {
        CurrentVehicle = new();
        CurrentVehicle.SetOwner(this);

        return CurrentVehicle.LoadFromDB(context, ActiveVehicleCoid, isInCharacterSelection);
    }

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostCharacter();
        Ghost.SetParent(this);
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateCharacterPacket charPacket)
        {
            charPacket.CurrentVehicleCoid = DBData.ActiveVehicleCoid ?? -1L;
            charPacket.CurrentTrailerCoid = -1L; // TODO
            charPacket.HeadId = HeadId;
            charPacket.BodyId = BodyId;
            charPacket.AccessoryId1 = DBData.HeadDetail1;
            charPacket.AccessoryId2 = DBData.HeadDetail2;
            charPacket.HairId = DBData.HairId;
            charPacket.MouthId = DBData.MouthId;
            charPacket.EyesId = DBData.EyesId;
            charPacket.HelmetId = DBData.HelmetId;
            charPacket.PrimaryColor = DBData.PrimaryColor;
            charPacket.SecondaryColor = DBData.SecondaryColor;
            charPacket.EyesColor = DBData.EyesColor;
            charPacket.HairColor = DBData.HairColor;
            charPacket.SkinColor = DBData.SkinColor;
            charPacket.SpecialityColor = DBData.SpecialityColor;
            charPacket.LastTownId = DBData.LastTownId;
            charPacket.LastStationMapId = DBData.LastStationMapId;
            charPacket.Level = DBData.Level;
            charPacket.UsingVehicle = Map != null && !Map.MapData.ContinentObject.IsTown;
            charPacket.UsingTrailer = false;
            charPacket.IsPosessingCreature = false;
            charPacket.GMLevel = GMLevel;
            charPacket.ServerTime = Environment.TickCount; // TODO
            charPacket.Name = Name;
            charPacket.ClanName = ClanMemberDBData?.Clan?.Name ?? "";
            charPacket.CharacterScaleOffset = DBData.ScaleOffset;
            
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"Character.WriteToPacket: Writing Name='{charPacket.Name}' (Length: {charPacket.Name?.Length ?? 0}), Level={charPacket.Level}");
        }

        if (packet is CreateCharacterExtendedPacket extendedCharPacket)
        {
            extendedCharPacket.CompletedMissionIds = CompletedMissionIds.ToList();
            extendedCharPacket.NumCompletedQuests = extendedCharPacket.CompletedMissionIds.Count;
            extendedCharPacket.NumCurrentQuests = CurrentQuests.Count;
            extendedCharPacket.CurrentQuests = CurrentQuests;
            extendedCharPacket.NumAchievements = 0;
            extendedCharPacket.NumDisciplines = 0;
            extendedCharPacket.NumSkills = 0;

            // Do NOT write live Credits into CreateCharacterExtended — non-zero values crash
            // the retail client. Restore after spawn via CurrencySync / CharacterLevel.
            CurrencySync.ClearCreateCharacterCredits(extendedCharPacket);

            WriteFirstTimeFlags(extendedCharPacket);
            WriteExploration(extendedCharPacket);

            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Debug,
                $"Character.WriteToPacket: coid={ObjectId.Coid} sending {CurrentQuests.Count} current quests [{string.Join(",", CurrentQuests.Select(q => q.MissionId))}], "
                + $"{extendedCharPacket.NumCompletedQuests} completed [{string.Join(",", extendedCharPacket.CompletedMissionIds)}]");
        }
    }

    public void EnterMap(SectorMap map, Vector3? position = null)
    {
        position ??= map.MapData.EntryPoint.ToVector3();

        DBData.LastTownId = map.ContinentId;

        Position = position.Value;
        Rotation = Quaternion.Default;

        DBData.PositionX = Position.X;
        DBData.PositionY = Position.Y;
        DBData.PositionZ = Position.Z;
        DBData.RotationX = Rotation.X;
        DBData.RotationY = Rotation.Y;
        DBData.RotationZ = Rotation.Z;
        DBData.RotationW = Rotation.W;

        CurrentVehicle.EnterMap(map, position);

        // TODO: save? new DB system? how to do it properly?
    }

    /// <summary>
    /// Copies live continent + pose into attached <see cref="CharacterData"/> / vehicle rows.
    /// Prefer vehicle pose when a current vehicle exists (authoritative while driving).
    /// Does not open a DB context — caller persists via <see cref="Managers.CharacterWorldStatePersistence"/>.
    /// </summary>
    /// <returns>Snapshot for persistence, or null when no DB row is attached.</returns>
    public CharacterWorldStateSnapshot? CaptureWorldStateToDb()
    {
        if (DBData == null)
            return null;

        if (Map != null)
            DBData.LastTownId = Map.ContinentId;

        Vector3 posePosition;
        Quaternion poseRotation;
        long vehicleCoid = -1;

        if (CurrentVehicle != null)
        {
            posePosition = CurrentVehicle.Position;
            poseRotation = CurrentVehicle.Rotation;
            vehicleCoid = CurrentVehicle.ObjectId.Coid;
            CurrentVehicle.CaptureWorldStateToDb(posePosition, poseRotation);
        }
        else
        {
            posePosition = Position;
            poseRotation = Rotation;
        }

        DBData.PositionX = posePosition.X;
        DBData.PositionY = posePosition.Y;
        DBData.PositionZ = posePosition.Z;
        DBData.RotationX = poseRotation.X;
        DBData.RotationY = poseRotation.Y;
        DBData.RotationZ = poseRotation.Z;
        DBData.RotationW = poseRotation.W;

        return new CharacterWorldStateSnapshot(
            DBData.Coid,
            DBData.LastTownId,
            posePosition.X,
            posePosition.Y,
            posePosition.Z,
            poseRotation.X,
            poseRotation.Y,
            poseRotation.Z,
            poseRotation.W,
            vehicleCoid);
    }
}
