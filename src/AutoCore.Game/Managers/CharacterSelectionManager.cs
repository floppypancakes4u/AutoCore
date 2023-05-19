namespace AutoCore.Game.Managers;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Login;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils.Memory;

public class CharacterSelectionManager : Singleton<CharacterSelectionManager>
{
    public static (bool, long) CreateNewCharacter(TNLConnection client, LoginNewCharacterPacket packet)
    {
        using var context = new CharContext();

        if (context.Characters.Any(c => c.Name == packet.CharacterName))
            return (false, -1);

        if (context.Vehicles.Any(v => v.Name == packet.VehicleName))
            return (false, -1);

        var cloneBaseCharacter = AssetManager.Instance.GetCloneBase<CloneBaseCharacter>(packet.CBID);
        if (cloneBaseCharacter == null)
            return (false, -1);

        var configNewCharacter = AssetManager.Instance.GetConfigNewCharacterFor(cloneBaseCharacter.CharacterSpecific.Race, cloneBaseCharacter.CharacterSpecific.Class);
        if (configNewCharacter == null)
            return (false, -1);

        var starterMapData = AssetManager.Instance.GetMapData(configNewCharacter.StartTown);
        if (starterMapData == null)
            return (false, -1);

        var characterSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Character,
            CBID = packet.CBID
        };

        var vehicleSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Vehicle,
            CBID = configNewCharacter.Vehicle
        };

        var wheelSetSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.WheelSet,
            CBID = packet.WheelsetCBID
        };

        var powerPlantSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.PowerPlant,
            CBID = configNewCharacter.PowerPlant
        };

        var armorSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Armor,
            CBID = configNewCharacter.Armor
        };

        var raceItemSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.RaceItem,
            CBID = configNewCharacter.RaceItem
        };

        var turretSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Weapon,
            CBID = configNewCharacter.Weapon
        };

        context.SimpleObjects.Add(characterSimpleObject);
        context.SimpleObjects.Add(vehicleSimpleObject);
        context.SimpleObjects.Add(wheelSetSimpleObject);
        context.SimpleObjects.Add(powerPlantSimpleObject);
        context.SimpleObjects.Add(armorSimpleObject);
        context.SimpleObjects.Add(raceItemSimpleObject);
        context.SimpleObjects.Add(turretSimpleObject);
        context.SaveChanges();

        try
        {
            var character = new CharacterData
            {
                Coid = characterSimpleObject.Coid,
                AccountId = client.Account.Id,
                Name = packet.CharacterName,
                HeadId = packet.HeadId,
                BodyId = packet.BodyId,
                HeadDetail1 = packet.HeadDetail1,
                HeadDetail2 = packet.HeadDetail2,
                HelmetId = packet.HelmetId,
                EyesId = packet.EyesId,
                MouthId = packet.MouthId,
                HairId = packet.HairId,
                PrimaryColor = packet.PrimaryColor,
                SecondaryColor = packet.SecondaryColor,
                EyesColor = packet.EyesColor,
                HairColor = packet.HairColor,
                SkinColor = packet.SkinColor,
                SpecialityColor = packet.SpecialityColor,
                ScaleOffset = packet.ScaleOffset,
                PositionX = 0.0f,
                PositionY = 0.0f,
                PositionZ = 0.0f,
                RotationX = 0.0f,
                RotationY = 0.0f,
                RotationZ = 0.0f,
                RotationW = 1.0f
            };
            context.Characters.Add(character);

            var vehicle = new VehicleData
            {
                Coid = vehicleSimpleObject.Coid,
                CharacterCoid = characterSimpleObject.Coid,
                Name = packet.VehicleName,
                PositionX = 0.0f,
                PositionY = 0.0f,
                PositionZ = 0.0f,
                RotationX = 0.0f,
                RotationY = 0.0f,
                RotationZ = 0.0f,
                RotationW = 1.0f,
                PrimaryColor = packet.VehiclePrimaryColor,
                SecondaryColor = packet.VehicleSecondaryColor,
                Trim = packet.VehicleTrim,
                Wheelset = wheelSetSimpleObject.Coid,
                PowerPlant = powerPlantSimpleObject.Coid,
                Armor = armorSimpleObject.Coid,
                RaceItem = raceItemSimpleObject.Coid,
                Turret = turretSimpleObject.Coid
            };
            context.Vehicles.Add(vehicle);
            context.SaveChanges();

            character.ActiveVehicleCoid = vehicleSimpleObject.Coid;
            context.SaveChanges();
        }
        catch
        {
            context.SimpleObjects.Remove(characterSimpleObject);
            context.SimpleObjects.Remove(vehicleSimpleObject);
            context.SimpleObjects.Remove(wheelSetSimpleObject);
            context.SimpleObjects.Remove(powerPlantSimpleObject);
            context.SimpleObjects.Remove(armorSimpleObject);
            context.SimpleObjects.Remove(raceItemSimpleObject);
            context.SimpleObjects.Remove(turretSimpleObject);
            context.SaveChanges();

            return (false, -1);
        }

        return (true, characterSimpleObject.Coid);
    }

    public static void DeleteCharacter(TNLConnection client, long coid)
    {
        using var context = new CharContext();

        var characterData = context.Characters.FirstOrDefault(c => c.AccountId == client.Account.Id && c.Coid == coid && c.Deleted == false);
        if (characterData != null)
        {
            characterData.Deleted = true;
            context.SaveChanges();
        }
    }

    public static void SendCharacterList(TNLConnection client)
    {
        using var context = new CharContext();

        var coids = context.Characters.Where(c => c.AccountId == client.Account.Id && c.Deleted == false).Select(c => c.Coid).ToList();

        foreach (var coid in coids)
            SendCharacter(client, context, coid);
    }

    public static void ExtendCharacterList(TNLConnection client, long coid)
    {
        using var context = new CharContext();

        SendCharacter(client, context, coid);
    }

    private static void SendCharacter(TNLConnection client, CharContext context, long coid)
    {
        var character = new Character(client, isInCharacterSelection: true);
        if (!character.LoadFromDB(context, coid))
            return;

        var vehicle = new Vehicle(isInCharacterSelection: true);
        if (!vehicle.LoadFromDB(context, character.ActiveVehicleCoid))
            return;

        var createCharPacket = new CreateCharacterPacket();
        character.WriteToPacket(createCharPacket);

        var createVehiclePacket = new CreateVehiclePacket();
        vehicle.WriteToPacket(createVehiclePacket);

        client.SendGamePacket(createCharPacket);
        client.SendGamePacket(createVehiclePacket);
    }
}
