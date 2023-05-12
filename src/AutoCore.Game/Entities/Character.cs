using Microsoft.EntityFrameworkCore;

namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

public class Character : Creature
{
    #region Properties
    #region Database Character Data
    private CharacterData DBData { get; set; }
    public string Name => DBData.Name;
    public long ActiveVehicleCoid => DBData.ActiveVehicleCoid;
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
    public byte Level => DBData.Level;
    #endregion

    #region Database Clan Data
    private ClanMember ClanMemberDBData { get; set; }
    public string ClanName => ClanMemberDBData?.Clan?.Name;
    public int ClanId => ClanMemberDBData?.ClanId ?? -1;
    public int ClanRank => ClanMemberDBData?.Rank ?? -1;
    #endregion

    public byte GMLevel { get; }
    public TNLConnection OwningConnection { get; }
    public Vehicle CurrentVehicle { get; private set; }
    public bool IsInCharacterSelection { get; }
    #endregion

    public Character(TNLConnection owningConnection, bool isInCharacterSelection = false)
    {
        OwningConnection = owningConnection;
        IsInCharacterSelection = isInCharacterSelection;
    }

    public override bool LoadFromDB(CharContext context, long coid)
    {
        SetCoid(coid, true);

        DBData = context.Characters.Include(c => c.SimpleObjectBase).FirstOrDefault(c => c.Coid == coid);
        if (DBData == null)
            return false;

        LoadCloneBase(DBData.SimpleObjectBase.CBID);

        ClanMemberDBData = context.ClanMembers.Include(cm => cm.Clan).FirstOrDefault(cm => cm.CharacterCoid == coid);

        // TODO: set up stuff, fields, baseclasses, etc

        return true;
    }

    public void LoadCurrentVehicle(CharContext context)
    {
        CurrentVehicle = new();
        if (!CurrentVehicle.LoadFromDB(context, ActiveVehicleCoid))
            throw new Exception("Unable to load active vehicle!");
    }

    public override void CreateGhost()
    {
        Ghost = new GhostCharacter();
        Ghost.SetParent(this);
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateCharacterPacket charPacket)
        {
            charPacket.CurrentVehicleCoid = DBData.ActiveVehicleCoid;
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
            charPacket.LastTownId = -1; // TODO
            charPacket.LastStationMapId = -1; // TODO
            charPacket.Level = DBData.Level;
            charPacket.UsingVehicle = false; // TODO
            charPacket.UsingTrailer = false;
            charPacket.IsPosessingCreature = false;
            charPacket.GMLevel = GMLevel;
            charPacket.ServerTime = DateTime.Now.Ticks; // TODO
            charPacket.Name = Name;
            charPacket.ClanName = ClanMemberDBData?.Clan?.Name ?? "";
            charPacket.CharacterScaleOffset = DBData.ScaleOffset;
        }

        if (packet is CreateCharacterExtendedPacket extendedCharPacket)
        {
            extendedCharPacket.NumCompletedQuests = 0;
            extendedCharPacket.NumCurrentQuests = 0;
            extendedCharPacket.NumAchievements = 0;
            extendedCharPacket.NumDisciplines = 0;
            extendedCharPacket.NumSkills = 0;

        }
    }
}
