namespace AutoCore.Game.CloneBases.Specifics;

using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public struct VehicleSpecific
{
    public float AVDCollisionSpinDamping { get; set; }
    public float AVDCollisionThreshold { get; set; }
    public float AVDNormalSpinDamping { get; set; }
    public float AbsoluteTopSpeed { get; set; }
    public float AerodynamicsAirDensity { get; set; }
    public float AerodynamicsDrag { get; set; }
    public Vector3 AerodynamicsExtraGravity { get; set; }
    public float AerodynamicsFrontalArea { get; set; }
    public float AerodynamicsLift { get; set; }
    public short ArmorAdd { get; set; }
    public float[] AxleScale { get; set; }
    public FrontRear BrakesMaxTorque { get; set; }
    public FrontRear BrakesMinBlockTime { get; set; }
    public FrontRear BrakesPedalInput { get; set; }
    public Vector3 CenterOfMassModifier { get; set; }
    public byte ClassType { get; set; }
    public float ClutchDelayTime { get; set; }
    public short CooldownAdd { get; set; }
    public RGB[] DefaultColors { get; set; }
    public int DefaultDriver { get; set; }
    public int DefaultWheelset { get; set; }
    public float DefensivePercent { get; set; }
    public short DownshiftRPM { get; set; }
    public byte[] DrawAxles { get; set; }
    public byte[] DrawShocks { get; set; }
    public byte EngineType { get; set; }
    public float[] GearRatios { get; set; }
    public int HardPointFacing { get; set; }
    public Vector3[] HardPoints { get; set; }
    public int HeatMaxAdd { get; set; }
    public Vector3 HitchPoint { get; set; }
    public short InventorySlots { get; set; }
    public float MaxTorqueFactor { get; set; }
    public float MaxWtArmor { get; set; }
    public float MaxWtEngine { get; set; }
    public float MaxWtWeaponDrop { get; set; }
    public float MaxWtWeaponFront { get; set; }
    public float MaxWtWeaponTurret { get; set; }
    public float MaximumRPMMax { get; set; }
    public float MaximumResistance { get; set; }
    public float MeleeScaler { get; set; }
    public float MinTorqueFactor { get; set; }
    public float MinimumRPM { get; set; }
    public float MinimumResistance { get; set; }
    public byte NumberOfGears { get; set; }
    public byte NumberOfTricks { get; set; }
    public byte NumberOfTrims { get; set; }
    public float OptimumRPMMax { get; set; }
    public float OptimumRPMMin { get; set; }
    public float OptimumResistance { get; set; }
    public int PowerMaxAdd { get; set; }
    public float PushBottomUp { get; set; }
    public float RVExtraAngularImpulse { get; set; }
    public float RVExtraTorqueFactor { get; set; }
    public float RVFrictionEqualizer { get; set; }
    public float RVInertiaPitch { get; set; }
    public float RVInertiaRoll { get; set; }
    public float RVInertiaYaw { get; set; }
    public float RVSpinTorquePitch { get; set; }
    public float RVSpinTorqueRoll { get; set; }
    public float RVSpinTorqueYaw { get; set; }
    public float RearWheelFrictionScalar { get; set; }
    public float ReverseGearRation { get; set; }
    public Vector3[] ShockAttachPoints { get; set; }
    public float ShockEffectThreshold { get; set; }
    public float[] ShockScale { get; set; }
    public Vector3 SkirtExtents { get; set; }
    public float SpeedLimiter { get; set; }
    public float SteeringFullSpeedLimit { get; set; }
    public float SteeringMaxAngle { get; set; }
    public FrontRear SuspensionDampeningCoefficientCompression { get; set; }
    public FrontRear SuspensionDampeningCoefficientExtension { get; set; }
    public FrontRear SuspensionLength { get; set; }
    public FrontRear SuspensionStrength { get; set; }
    public short TorqueMax { get; set; }
    public float TransmissionRatio { get; set; }
    public VehicleTrick[] Tricks { get; set; }
    public byte TurretSize { get; set; }
    public short UpshiftRPM { get; set; }
    public short VehicleFlags { get; set; }
    public byte VehicleType { get; set; }
    public byte WheelAxle { get; set; }
    public byte WheelExistance { get; set; }
    public Vector3[] WheelHardPoints { get; set; }
    public float[] WheelRadius { get; set; }
    public FrontRear WheelTorqueRatios { get; set; }
    public float[] WheelWidth { get; set; }

    public static VehicleSpecific ReadNew(BinaryReader reader)
    {
        var vs = new VehicleSpecific
        {
            VehicleType = reader.ReadByte(),
            ClassType = reader.ReadByte(),
        };

        reader.ReadBytes(2);

        vs.DefaultColors = new RGB[3];
        for (var i = 0; i < 3; ++i)
            vs.DefaultColors[i] = RGB.ReadNew(reader);

        vs.HardPoints = new Vector3[3];
        for (var i = 0; i < 3; ++i)
            vs.HardPoints[i] = Vector3.ReadNew(reader);

        vs.HardPointFacing = reader.ReadInt32();
        vs.WheelExistance = reader.ReadByte();
        vs.WheelAxle = reader.ReadByte();

        reader.ReadBytes(2);

        vs.WheelHardPoints = new Vector3[6];
        for (var i = 0; i < 6; ++i)
            vs.WheelHardPoints[i] = Vector3.ReadNew(reader);

        vs.SuspensionLength = FrontRear.ReadNew(reader);
        vs.SuspensionStrength = FrontRear.ReadNew(reader);
        vs.SuspensionDampeningCoefficientCompression = FrontRear.ReadNew(reader);
        vs.SuspensionDampeningCoefficientExtension = FrontRear.ReadNew(reader);
        vs.BrakesMaxTorque = FrontRear.ReadNew(reader);
        vs.BrakesMinBlockTime = FrontRear.ReadNew(reader);
        vs.BrakesPedalInput = FrontRear.ReadNew(reader);
        vs.SteeringMaxAngle = reader.ReadSingle();
        vs.SteeringFullSpeedLimit = reader.ReadSingle();
        vs.AerodynamicsFrontalArea = reader.ReadSingle();
        vs.AerodynamicsDrag = reader.ReadSingle();
        vs.AerodynamicsLift = reader.ReadSingle();
        vs.AerodynamicsAirDensity = reader.ReadSingle();
        vs.AerodynamicsExtraGravity = Vector3.ReadNew(reader);
        vs.AVDNormalSpinDamping = reader.ReadSingle();
        vs.AVDCollisionSpinDamping = reader.ReadSingle();
        vs.AVDCollisionThreshold = reader.ReadSingle();
        vs.RVFrictionEqualizer = reader.ReadSingle();
        vs.RVSpinTorqueRoll = reader.ReadSingle();
        vs.RVSpinTorquePitch = reader.ReadSingle();
        vs.RVSpinTorqueYaw = reader.ReadSingle();
        vs.RVExtraAngularImpulse = reader.ReadSingle();
        vs.RVExtraTorqueFactor = reader.ReadSingle();
        vs.RVInertiaRoll = reader.ReadSingle();
        vs.RVInertiaPitch = reader.ReadSingle();
        vs.RVInertiaYaw = reader.ReadSingle();
        vs.WheelTorqueRatios = FrontRear.ReadNew(reader);
        vs.VehicleFlags = reader.ReadInt16();

        reader.ReadBytes(2);

        vs.HitchPoint = Vector3.ReadNew(reader);
        vs.WheelRadius = reader.ReadConstArray(6, reader.ReadSingle);
        vs.WheelWidth = reader.ReadConstArray(6, reader.ReadSingle);
        vs.SpeedLimiter = reader.ReadSingle();
        vs.AbsoluteTopSpeed = reader.ReadSingle();

        vs.ShockAttachPoints = new Vector3[6];
        for (var i = 0; i < 6; ++i)
            vs.ShockAttachPoints[i] = Vector3.ReadNew(reader);

        vs.DrawAxles = reader.ReadBytes(2);
        vs.DrawShocks = reader.ReadBytes(2);
        vs.AxleScale = reader.ReadConstArray(2, reader.ReadSingle);
        vs.ShockScale = reader.ReadConstArray(2, reader.ReadSingle);
        vs.ShockEffectThreshold = reader.ReadSingle();
        vs.EngineType = reader.ReadByte();
        vs.NumberOfGears = reader.ReadByte();
        vs.TorqueMax = reader.ReadInt16();
        vs.DownshiftRPM = reader.ReadInt16();
        vs.UpshiftRPM = reader.ReadInt16();
        vs.MinTorqueFactor = reader.ReadSingle();
        vs.MaxTorqueFactor = reader.ReadSingle();
        vs.MinimumRPM = reader.ReadSingle();
        vs.OptimumRPMMin = reader.ReadSingle();
        vs.OptimumRPMMax = reader.ReadSingle();
        vs.MaximumRPMMax = reader.ReadSingle();
        vs.MinimumResistance = reader.ReadSingle();
        vs.OptimumResistance = reader.ReadSingle();
        vs.MaximumResistance = reader.ReadSingle();
        vs.TransmissionRatio = reader.ReadSingle();
        vs.ClutchDelayTime = reader.ReadSingle();
        vs.ReverseGearRation = reader.ReadSingle();
        vs.GearRatios = reader.ReadConstArray(5, reader.ReadSingle);
        vs.ArmorAdd = reader.ReadInt16();

        reader.ReadBytes(2);

        vs.PowerMaxAdd = reader.ReadInt32();
        vs.HeatMaxAdd = reader.ReadInt32();
        vs.CooldownAdd = reader.ReadInt16();

        reader.ReadBytes(2);

        vs.DefaultWheelset = reader.ReadInt32();
        vs.DefaultDriver = reader.ReadInt32();
        vs.MaxWtWeaponFront = reader.ReadSingle();
        vs.MaxWtWeaponTurret = reader.ReadSingle();
        vs.MaxWtWeaponDrop = reader.ReadSingle();
        vs.MaxWtArmor = reader.ReadSingle();
        vs.MaxWtEngine = reader.ReadSingle();
        vs.DefensivePercent = reader.ReadSingle();
        vs.TurretSize = reader.ReadByte();
        vs.NumberOfTrims = reader.ReadByte();
        vs.NumberOfTricks = reader.ReadByte();

        reader.ReadByte();

        vs.MeleeScaler = reader.ReadSingle();
        vs.InventorySlots = reader.ReadInt16();

        reader.ReadBytes(6);

        vs.SkirtExtents = Vector3.ReadNew(reader);
        vs.PushBottomUp = reader.ReadSingle();
        vs.CenterOfMassModifier = Vector3.ReadNew(reader);
        vs.RearWheelFrictionScalar = reader.ReadSingle();

        return vs;
    }
}
