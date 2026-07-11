using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.TNL;
using AutoCore.Utils;

public class GhostVehicle : GhostObject
{
    private static NetClassRepInstance<GhostVehicle> _dynClassRep;

    /// <summary>Live A/B lever for the NPC AI-state wire block (Risk 5). Flip to false to fall
    /// back to not sending driver AI state if this proves destabilizing in production.</summary>
    public static bool EnableAiStateWire = true;

    /// <summary>Isolation lever: when false, always omit the optional path block (flag false).</summary>
    public static bool EnablePathWire = true;

    /// <summary>Isolation lever: when false, always omit the CurrentOwner block (flag false).</summary>
    public static bool EnableOwnerWire = true;

    /// <summary>Isolation lever: when false, force TemplateId and SpawnOwner flags false.</summary>
    public static bool EnableTemplateSpawnWire = true;

    /// <summary>
    /// Phase-2 recovery profile for foreign NPC vehicles. Initial packs keep the required body
    /// plus pose; later updates are restricted to pose and wheel hardpoint (CreateVehicle equip
    /// can leave +0x258 null — delta WheelSet hardpoint is the client recovery path).
    /// Disabled by default.
    /// </summary>
    public static bool EnableMinimalForeignInitialProfile = false;

    /// <summary>
    /// Allows the documented path block during the minimal foreign-vehicle initial profile.
    /// This is a separate experiment because paths are initial-only and have client lifetime
    /// requirements distinct from pose replication.
    /// </summary>
    public static bool EnableMinimalForeignPathBlock = false;

    /// <summary>Allows template and spawn-owner fields during the minimal foreign initial profile.</summary>
    public static bool EnableMinimalForeignTemplateSpawnBlock = false;

    /// <summary>Allows the initial owner/driver construction block during the minimal profile.</summary>
    public static bool EnableMinimalForeignOwnerBlock = false;

    /// <summary>
    /// When true, pack the WheelSet hardpoint on <b>initial</b> ghost updates (default: false).
    /// Client RE: initial hardpoint CBID is written into the ghost create buffer at +0x45c —
    /// the same field <c>EquipFromCreate</c> reads when materializing from ghost. This is
    /// <b>not</b> a same-call SetWheelset; it seeds the create shell so ghost-first materialize
    /// can GiveItem/SetWheelset. Does not fix a live vehicle that already equip-failed with CBID 0.
    /// Off by default — isolation experiment only (see OWNER_WHEEL_RACE_RE §11).
    /// </summary>
    public static bool EnableInitialHardpointPack = false;

    /// <summary>
    /// Priority-1 owner-on experiment: on foreign (global) <b>initial</b> packs, omit
    /// <see cref="PositionMask"/> so client unpack does not run
    /// <c>Vehicle_setDrivingInputs</c>→activate while <c>+0x258</c> may still be null.
    /// Position stays dirty and ships on a later delta. Default false.
    /// </summary>
    public static bool EnableDeferredForeignPose = false;

    /// <summary>
    /// Priority-2: first foreign ghost initial withholds owner; after delay, descope once then
    /// rescope so TNL sends a second initial with owner (and pose). Default false.
    /// </summary>
    public static bool EnableForeignReghostOwner = false;

    /// <summary>
    /// When true (default), foreign/other vehicles use a higher TNL ghost update weight than
    /// generic props so pose deltas ship more often (smoother NPC patrol). Disable to A/B.
    /// </summary>
    public static bool EnableForeignVehiclePosePriorityBoost = true;

    /// <summary>Type weight for vehicles when <see cref="EnableForeignVehiclePosePriorityBoost"/> is on.</summary>
    internal const float VehiclePosePriorityWeight = 0.40f;

    private const int CoidCurrentPathBits = 18;

    public override float GetUpdatePriority(NetObject scopeObject, ulong updateMask, int updateSkips)
    {
        // Same pin rules as GhostObject (self / viewer target).
        if (ReferenceEquals(this, scopeObject))
            return 1.0f;

        var viewer = GetViewerParent(scopeObject);
        if (Parent != null && viewer != null && ReferenceEquals(viewer.Target, Parent))
            return 1.0f;

        if (!EnableForeignVehiclePosePriorityBoost || Parent == null || viewer == null)
            return base.GetUpdatePriority(scopeObject, updateMask, updateSkips);

        var dx = viewer.Position.X - Parent.Position.X;
        var dz = viewer.Position.Z - Parent.Position.Z;
        var distance = (float)Math.Sqrt((dx * dx) + (dz * dz));
        var falloff = Math.Clamp(1.0f - (distance / InterestSelector.BaseScopeDropRadius), 0.0f, 1.0f);
        return (VehiclePosePriorityWeight * falloff) + (updateSkips * 0.01f);
    }

    /// <summary>
    /// Latched at each initial pack: was the CurrentOwner block actually written on THIS ghost's
    /// most recent initial update? Delta packs gate owner-applied bytes (GM nibble → owner+0x12A,
    /// AI state → owner+0x127, attribute payload) on this latch instead of recomputing from the
    /// current lever/owner state. Recomputing lets a live <c>wire set EnableOwnerWire false</c>
    /// during ghost create (owner block omitted), then a flip back to true, write owner bytes to a
    /// client whose vehicle has no owner object — the null-owner access violation (0x005F8FED) the
    /// PackUpdate delta comment warns about.
    /// </summary>
    private bool _ownerSentAtInitial;

    public const ulong AttributeMask    = 0x0000200000ul;
    public const ulong ClanMask         = 0x0000400000ul;
    public const ulong PetCBIDMask      = 0x0001000000ul;
    public const ulong ShieldMaxMask    = 0x0002000000ul;
    public const ulong ShieldMask       = 0x0004000000ul;
    public const ulong PowerMask        = 0x0008000000ul;
    public const ulong GMMask           = 0x0010000000ul;
    public const ulong HeatMask         = 0x0020000000ul;
    public const ulong ChangeArmor      = 0x0040000000ul;
    public const ulong StateMask        = 0x0080000000ul;
    public const ulong WheelSetMask     = 0x0100000000ul;

    public const ulong FrontWeaponMask  = 0x0400000000ul;
    public const ulong TurretWeaponMask = 0x0800000000ul;
    public const ulong RearWeaponMask   = 0x1000000000ul;
    public const ulong MeleeWeaponMask  = 0x2000000000ul;
    public const ulong OrnamentMask     = 0x4000000000ul;

    public override NetClassRep GetClassRep()
    {
        return _dynClassRep;
    }

    public new static void RegisterNetClassReps()
    {
        ImplementNetObject(out _dynClassRep);
    }

    public GhostVehicle()
    {
        UpdatePriorityScalar = 1.0f;
    }

    public override void SetParent(ClonedObjectBase parent)
    {
        base.SetParent(parent);

        if (parent == null || parent.GetAsVehicle() == null)
            return;

        var vehParent = parent.GetAsVehicle();
        var superCharacter = parent.GetSuperCharacter(false);

        // TODO?
    }

    public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
    {
        if (Parent == null)
            throw new Exception("Missing parent for GhostVehicle!");

        var ret = 0ul;
        var startBits = stream.GetBitPosition();
        var isInitial = PIsInitialUpdate;
        var sourceUpdateMask = updateMask;

        var parentVehicle = Parent.GetAsVehicle();
        var useMinimalForeignInitialProfile = Parent.ObjectId.Global
            && EnableMinimalForeignInitialProfile;

        // Minimal foreign: initial packs pose-only (optional initial blocks are separate levers).
        // Deltas also admit WheelSetMask so a hardpoint can materialize +0x258 when Path A equip
        // saw nested wheel CBID 0 (client zero-fill) despite a good server CreateVehicle.
        // EnableInitialHardpointPack additionally admits WheelSet on initial so create-buffer
        // +0x45c can be seeded before ghost materialize (EquipFromCreate).
        // EnableDeferredForeignPose strips PositionMask from foreign initial (activate race).
        var deferForeignPose = Parent.ObjectId.Global && EnableDeferredForeignPose;
        if (useMinimalForeignInitialProfile)
        {
            var initialPose = deferForeignPose ? 0UL : PositionMask;
            updateMask = isInitial
                ? initialPose | (EnableInitialHardpointPack ? WheelSetMask : 0UL)
                : updateMask & (PositionMask | WheelSetMask);
        }
        else if (isInitial && deferForeignPose)
        {
            updateMask &= ~PositionMask;
        }

        // A connection-scoped override is deliberately limited to foreign initial updates. It
        // provides a deterministic recovery harness without changing the stream for any other
        // player or for later deltas.
        if (isInitial
            && Parent.ObjectId.Global
            && connection is TNLConnection { ForeignVehicleInitialMaskOverrideForTests: ulong overrideMask })
        {
            updateMask = overrideMask;
        }

        var owner = parentVehicle.Owner;

        var wouldPackPath = parentVehicle.CoidCurrentPath > 0;
        var wouldPackOwner = owner != null;
        var wouldPackTemplate = parentVehicle.TemplateId != -1;
        var wouldPackSpawnOwner = parentVehicle.SpawnOwnerCoid != -1;
        var packPath = (!useMinimalForeignInitialProfile || EnableMinimalForeignPathBlock)
            && EnablePathWire
            && wouldPackPath;
        var suppressOwnerForReghost = connection is TNLConnection reghostConn
            && reghostConn.ShouldSuppressForeignOwnerOnPack(Parent.ObjectId.Coid);
        var packOwner = (!useMinimalForeignInitialProfile || EnableMinimalForeignOwnerBlock)
            && EnableOwnerWire
            && wouldPackOwner
            && !suppressOwnerForReghost;
        var allowTemplateSpawn = !useMinimalForeignInitialProfile || EnableMinimalForeignTemplateSpawnBlock;
        var packTemplate = allowTemplateSpawn && EnableTemplateSpawnWire && wouldPackTemplate;
        var packSpawnOwner = allowTemplateSpawn && EnableTemplateSpawnWire && wouldPackSpawnOwner;

        if (PIsInitialUpdate)
        {
            PackCommon(stream);

            stream.Write(parentVehicle.PrimaryColor);
            stream.Write(parentVehicle.SecondaryColor);

            stream.WriteFlag(!Parent.Map.MapData.ContinentObject.IsTown); // IsActive

            stream.Write(parentVehicle.Trim);

            // Optional multiplier bodies are not wired yet — always flag false (client uses defaults).
            stream.WriteFlag(false); // SpeedAdd == 1.0f
            stream.WriteFlag(false); // BrakesMaxTorqueFrontMultiplier == 1.0f
            stream.WriteFlag(false); // BrakesMaxTorqueRearAdjustMultiplier == 1.0f
            stream.WriteFlag(false); // SteeringMaxAngleMultiplier == 1.0f
            stream.WriteFlag(false); // SteeringFullSpeedLimitMultiplier == 1.0f
            stream.WriteFlag(false); // AVDCollisionSpinDampeningMultiplier == 1.0f
            stream.WriteFlag(false); // AVDNormalSpinDampeningMultiplier == 1.0f

            if (stream.WriteFlag(packPath)) // path
            {
                var currentPath = parentVehicle.CoidCurrentPath;

                if (currentPath >= (1 << CoidCurrentPathBits))
                    Logger.WriteLog(LogType.Error, $"GhostVehicle.PackUpdate: CoidCurrentPath {currentPath} exceeds the {CoidCurrentPathBits}-bit wire field and will be truncated.");

                stream.WriteInt((uint)currentPath, CoidCurrentPathBits); // CoidCurrentPathID
                stream.Write(parentVehicle.ExtraPathId); // ExtraPathId
                stream.WriteFlag(parentVehicle.PathReversing); // PathReversing
                stream.WriteFlag(parentVehicle.PathIsRoad); // PathIsRoad
                stream.Write(parentVehicle.PatrolDistance); // PatrolDistance
            }

            if (stream.WriteFlag(packTemplate)) // TemplateId != -1
            {
                stream.WriteInt((uint)parentVehicle.TemplateId, 20); // TemplateId
            }

            if (stream.WriteFlag(packSpawnOwner)) // CoidSpawnOwner != -1
            {
                stream.WriteInt((uint)parentVehicle.SpawnOwnerCoid, 20); // CoidSpawnOwner
            }

            stream.WriteBits(8, BitConverter.GetBytes(0)); // trick count (no trick ids when zero)

            stream.WriteFlag(false); // IsTrailer

            // Latch whether this client's initial carried the owner block; deltas read this to
            // decide if owner-applied bytes are safe to write (see field doc / Risk-5 lever notes).
            _ownerSentAtInitial = packOwner;

            if (stream.WriteFlag(packOwner)) // CurrentOwner
            {
                stream.Write(owner.ObjectId.Coid); // CurrentOwner coid
                stream.WriteFlag(owner.ObjectId.Global); // CurrentOwner global
                stream.WriteInt((uint)owner.CBID, 20); // CurrentOwner CBID

                var ownerAsCharacter = owner.GetAsCharacter();

                if (stream.WriteFlag(ownerAsCharacter != null))
                {
                    stream.WriteString(ownerAsCharacter.Name, 17);
                    stream.WriteString(ownerAsCharacter.ClanName, 51);
                    stream.Write(ownerAsCharacter.Level);
                    stream.WriteFlag(false); // IsPossessingCreature
                    stream.WriteString(parentVehicle.Name, 33);
                    stream.WriteInt((uint)ownerAsCharacter.HeadId, 16);
                    stream.WriteInt((uint)ownerAsCharacter.BodyId, 16);
                    stream.WriteInt((uint)ownerAsCharacter.HeadDetail1, 16);
                    stream.WriteInt((uint)ownerAsCharacter.HeadDetail2, 16);
                    stream.WriteInt((uint)ownerAsCharacter.MouthId, 16);
                    stream.WriteInt((uint)ownerAsCharacter.EyesId, 16);
                    stream.WriteInt((uint)ownerAsCharacter.HelmetId, 16);
                    stream.WriteInt((uint)ownerAsCharacter.HairId, 16);
                }
                else
                {
                    // Retail VehicleNet_UnpackGhostVehicle creature-owner form has no SpawnOwner
                    // slot (unlike GhostCreature). Optional fields then DoesntCountAsSummon + level + elite.
                    stream.WriteFlag(false); // EnhancementID == -1
                    stream.WriteFlag(false); // CoidOnUseTrigger == -1
                    stream.WriteFlag(false); // CoidOnUseReaction == -1
                    stream.WriteFlag(false); // CreatureSummoner coid == -1

                    stream.WriteFlag(false); // DoesntCountAsSummon
                    stream.WriteBits(8, new byte[] { owner.GetAsCreature()?.Level ?? 0 }); // Level (driver level for NPC-driven vehicles)
                    stream.WriteFlag(false); // IsElite
                }
            }

            ret = 0x80;
        }
        else
        {
            if (stream.WriteFlag((updateMask & SkillsMask) != 0))
            {
                stream.WriteFlag(false); // Has Owner Skills (not wired yet)
                PackSkills(stream, parentVehicle);
            }
        }

        // Initial hardpoints are not applied as immediate SetWheelset (see OWNER_WHEEL_RACE_RE §10–11).
        // On initial they write into the ghost create buffer (wheel CBID → +0x45c). EquipFromCreate
        // reads that field when FUN_008078b0 materializes from ghost — so packing WheelSet on
        // initial can fix ghost-first materialize, but not a live vehicle that already failed nest equip.
        // Delta hardpoints take the PostCorrection / later equip path.
        // Default: skip all initial equipment. EnableInitialHardpointPack: WheelSet only on initial.
        var packEquipment = !isInitial;
        var packWheelHardpoint = packEquipment
            || (isInitial && EnableInitialHardpointPack);

        PackHardpoint(stream, packWheelHardpoint && (updateMask & WheelSetMask) != 0, parentVehicle.WheelSet);
        PackHardpoint(stream, packEquipment && (updateMask & FrontWeaponMask) != 0, parentVehicle.WeaponFront);
        PackHardpoint(stream, packEquipment && (updateMask & TurretWeaponMask) != 0, parentVehicle.WeaponTurret);
        PackHardpoint(stream, packEquipment && (updateMask & RearWeaponMask) != 0, parentVehicle.WeaponRear);
        PackHardpoint(stream, packEquipment && (updateMask & MeleeWeaponMask) != 0, parentVehicle.WeaponMelee);
        PackHardpoint(stream, packEquipment && (updateMask & OrnamentMask) != 0, parentVehicle.Ornament);

        if (stream.WriteFlag(packEquipment && (updateMask & ChangeArmor) != 0) && stream.WriteFlag(parentVehicle.Armor != null))
        {
            stream.WriteInt((uint)parentVehicle.Armor.CBID, 20);
            stream.Write(parentVehicle.Armor.ObjectId.Coid);
            stream.WriteFlag(parentVehicle.Armor.ObjectId.Global);
            // Client reads six 16-bit resistances (not floats) — DamageSpecific is short[6].
            var resists = parentVehicle.Armor.CloneBaseArmor?.ArmorSpecific.Resistances.Damage;
            for (var i = 0; i < 6; ++i)
                stream.Write(resists != null && i < resists.Length ? resists[i] : (short)0);
        }

        // Client VehicleNet_UnpackGhostVehicle applies GM (4 bits → owner+0x12A) and AI state
        // (8 bits → owner+0x127) with no null check. Packing these without a client-side owner
        // object is an access violation (e.g. 0x005F8FED). Game-wide rule: only pack when the
        // owner block is on the wire for this initial, or owner already exists for deltas.
        var characterOwner = owner?.GetAsCharacter();
        var driverCreature = characterOwner == null ? owner?.GetAsCreature() : null;
        // On a delta, "does the client have an owner object" is answered by what THIS ghost sent at
        // its initial pack (_ownerSentAtInitial), NOT by whether an owner exists server-side right
        // now — the latter would write owner bytes to a client that never received the owner block.
        var clientHasOwner = isInitial ? packOwner : _ownerSentAtInitial;

        if (stream.WriteFlag((updateMask & GMMask) != 0 && characterOwner != null && clientHasOwner))
        {
            stream.WriteInt(characterOwner.GMLevel, 4);
        }

        // Clan / pet CBID payloads not wired yet — always flag false to hold the wire slot.
        stream.WriteFlag(false); // ClanMask TODO
        stream.WriteFlag(false); // PetCBIDMask TODO

        if (stream.WriteFlag((updateMask & MurdererMask) != 0)) // TODO
        {
            stream.WriteBits(64, BitConverter.GetBytes((long)0)); // Coid murderer
        }

        if (stream.WriteFlag((updateMask & HealthMask) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetCurrentHP(), 0), 18);
            stream.WriteFlag(Parent.GetIsCorpse());
        }

        if (stream.WriteFlag((updateMask & HealthMaxMask) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetMaximumHP(), 0), 18);
        }

        if (stream.WriteFlag((updateMask & StateMask) != 0 && EnableAiStateWire && driverCreature != null && clientHasOwner))
        {
            stream.WriteBits(8, new byte[] { driverCreature.AiCombatState }); // AI State if owner is creature
        }

        if (stream.WriteFlag((updateMask & PositionMask) != 0))
        {
            stream.Write(parentVehicle.Position.X);
            stream.Write(parentVehicle.Position.Y);
            stream.Write(parentVehicle.Position.Z);

            stream.Write(parentVehicle.Rotation.X);
            stream.Write(parentVehicle.Rotation.Y);
            stream.Write(parentVehicle.Rotation.Z);
            stream.Write(parentVehicle.Rotation.W);

            stream.Write(parentVehicle.Velocity.X);
            stream.Write(parentVehicle.Velocity.Y);
            stream.Write(parentVehicle.Velocity.Z);

            stream.Write(parentVehicle.AngularVelocity.X);
            stream.Write(parentVehicle.AngularVelocity.Y);
            stream.Write(parentVehicle.AngularVelocity.Z);

            stream.Write((byte)parentVehicle.VehicleFlags);
            stream.Write(parentVehicle.Firing);

            stream.WriteSignedFloat(parentVehicle.Acceleration, 6);
            stream.WriteSignedFloat(parentVehicle.Steering, 6);

            stream.Write(parentVehicle.WantedTurretDirection);
        }

        if (stream.WriteFlag((updateMask & TargetMask) != 0))
        {
            if (Parent.Target != null)
            {
                stream.Write(Parent.Target.ObjectId.Coid);
                stream.WriteFlag(Parent.Target.ObjectId.Global);
            }
            else
            {
                stream.Write((long)0);
                stream.WriteFlag(false);
            }
        }

        if (stream.WriteFlag(characterOwner != null && clientHasOwner && (updateMask & AttributeMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribCombat
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribPerception
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribTech
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribTheory
        }

        if (stream.WriteFlag((updateMask & HeatMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // TODO
        }

        if (stream.WriteFlag((updateMask & ShieldMaxMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(100)); // MaxShield
        }

        if (stream.WriteFlag((updateMask & ShieldMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // Shield
        }

        if (stream.WriteFlag((updateMask & PowerMask) != 0)) // TODO
        {
            stream.WriteBits(32, BitConverter.GetBytes(100)); // Power
        }

        if (stream.WriteFlag((updateMask & TokenMask) != 0))
        {
            stream.WriteFlag(false); // GivesToken
        }

        if (WireDiag.Enabled)
        {
            var playerCoid = 0L;
            if (connection is TNLConnection tnl)
                playerCoid = tnl.CurrentCharacter?.ObjectId.Coid ?? tnl.GetPlayerCOID();

            var bits = (int)(stream.GetBitPosition() - startBits);
            WireDiag.RecordGhostPack(
                name: "GhostVehicle",
                coid: Parent.ObjectId.Coid,
                bits: bits,
                mask: updateMask,
                initial: isInitial,
                playerCoid: playerCoid,
                detail: $"path={(packPath ? 1 : 0)}/{(wouldPackPath ? 1 : 0)} owner={(packOwner ? 1 : 0)}/{(wouldPackOwner ? 1 : 0)} " +
                        $"tmpl={(packTemplate ? 1 : 0)}/{(wouldPackTemplate ? 1 : 0)} spawn={(packSpawnOwner ? 1 : 0)}/{(wouldPackSpawnOwner ? 1 : 0)} " +
                        $"clientOwner={(clientHasOwner ? 1 : 0)} equip={(packEquipment ? 1 : 0)} " +
                        $"initWheel={(isInitial && EnableInitialHardpointPack ? 1 : 0)} " +
                        $"deferPose={(isInitial && deferForeignPose ? 1 : 0)} " +
                        $"aiWire={(EnableAiStateWire ? 1 : 0)} global={(Parent.ObjectId.Global ? 1 : 0)} " +
                        $"profile={(useMinimalForeignInitialProfile ? "minimal" : "full")} sourceMask={sourceUpdateMask:X}");
        }

        // TNL treats ret as "still dirty". Minimal initial packs strip WheelSet from the effective
        // mask; if we returned only ret (0x80), WheelSet would be cleared without ever shipping.
        // Keep it dirty so the next delta can run the hardpoint recovery path.
        if (useMinimalForeignInitialProfile
            && (sourceUpdateMask & WheelSetMask) != 0
            && (updateMask & WheelSetMask) == 0)
        {
            ret |= WheelSetMask;
        }

        // Deferred pose: first foreign initial omitted PositionMask — keep dirty for next delta.
        if (isInitial
            && deferForeignPose
            && (sourceUpdateMask & PositionMask) != 0
            && (updateMask & PositionMask) == 0)
        {
            ret |= PositionMask;
        }

        return ret;
    }

    /// <summary>
    /// Wheel/weapon/ornament hardpoint: mask flag, then present flag + CBID20 + coid64 + global.
    /// </summary>
    private static void PackHardpoint(BitStream stream, bool maskSet, ClonedObjectBase item)
    {
        if (stream.WriteFlag(maskSet) && stream.WriteFlag(item != null))
        {
            stream.WriteInt((uint)item.CBID, 20);
            stream.Write(item.ObjectId.Coid);
            stream.WriteFlag(item.ObjectId.Global);
        }
    }

}
