using Microsoft.EntityFrameworkCore;

namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

public class Character : Creature
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
    public int LastTownId => DBData.LastTownId;
    public int LastStationMapId => DBData.LastStationMapId;
    public int LastStationId => DBData.LastStationId;
    public new byte Level => DBData.Level;
    #endregion

    public override byte GetLevel() => Level;

    #region Database Clan Data
    private ClanMember ClanMemberDBData { get; set; }
    public string ClanName => ClanMemberDBData?.Clan?.Name;
    public int ClanId => ClanMemberDBData?.ClanId ?? -1;
    public int ClanRank => ClanMemberDBData?.Rank ?? -1;
    #endregion

    public byte GMLevel { get; set; }
    public TNLConnection OwningConnection { get; private set; }
    public Vehicle CurrentVehicle { get; private set; }

    // Mission tracking
    public List<CharacterQuest> CurrentQuests { get; } = new();
    #endregion

    public Character()
    {
        // TODO: Add the starting mission once we figure out the correct packet structure
        // The 72-byte SVOGCharacterObjective structure needs to be reverse-engineered
        // CurrentQuests.Add(new CharacterQuest(554, 714, 0));
    }

    public void SetOwningConnection(TNLConnection owningConnection)
    {
        OwningConnection = owningConnection;
    }

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
            extendedCharPacket.NumCompletedQuests = 0;
            extendedCharPacket.NumCurrentQuests = CurrentQuests.Count;
            extendedCharPacket.CurrentQuests = CurrentQuests;
            extendedCharPacket.NumAchievements = 0;
            extendedCharPacket.NumDisciplines = 0;
            extendedCharPacket.NumSkills = 0;

            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Debug, $"Character.WriteToPacket: Sending {CurrentQuests.Count} current quests");
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
}
