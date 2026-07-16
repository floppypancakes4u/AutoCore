# Server handbrake wire — `Firing` vs `VehicleFlags` packing order

**Program:** `autoassault.exe` (image base `0x400000`)  
**Verified:** 2026-07-15  
**Scope:** Ghost vehicle **PositionMask** pose block only (server → client `GhostVehicle.PackUpdate`).  
**Sources:** `GhostVehicleWireRegressionTests`, `GhostVehicle` pack comments, client unpack anchors, drive-axis RE.  
**No production code in this note.**

---

## 1. Verdict (do not reverse)

On the **ghost pose** wire, after linVel + angVel, the two flag bytes are:

| Wire order | Server field | Client meaning |
|---|---|---|
| **Byte #1 (first)** | `Vehicle.Firing` | Weapon hardpoint enable set |
| **Byte #2 (second)** | `Vehicle.VehicleFlags` (`VehicleMovedFlags`) | Driving flags; **bit0 = Handbreak** |

```
… angVel.X/Y/Z …
Write(Firing)              // byte #1
Write((byte)VehicleFlags)  // byte #2
WriteSignedFloat(Acceleration, 6)
WriteSignedFloat(Steering, 6)
Write(WantedTurretDirection)
```

**Correct pack (server):**

```csharp
stream.Write(parentVehicle.Firing);
stream.Write((byte)parentVehicle.VehicleFlags);
```

**Wrong pack (historical bug):**

```csharp
// DO NOT — swaps fire into handbrake
stream.Write((byte)parentVehicle.VehicleFlags);
stream.Write(parentVehicle.Firing);
```

---

## 2. Bit layouts

### 2.1 `Firing` (byte #1 — weapon enable)

Retail client pose unpack (`VehicleNet_UnpackGhostVehicle` @ `0x5F7720`) treats this as the
weapon-hardpoint enable set:

| Bit | Role |
|---|---|
| `0x01` | Front weapon firing |
| `0x02` | Turret firing |
| `0x04` | Rear weapon firing |

Unrelated to drive axes. Combat dirties `PositionMask` so observers get aim + fire bits on the
same pose pack (`Vehicle.SetCombatWeaponWire`).

### 2.2 `VehicleFlags` / `VehicleMovedFlags` (byte #2 — driving flags)

Server enum (`VehicleMovedPacket.cs`):

| Flag | Value | Client / physics role |
|---|---|---|
| `Handbreak` | `0x01` | Written to entity **handbrake / sharp** byte `vehicle+0x61C` |
| `SimClient` | `0x02` | Simulation / ownership-related (not handbrake) |
| `Corpse` | `0x04` | Corpse marker on driving-flags nibble |

Retail name on the client path is **Handbreak** (spelling as in wire enum). That bit is what
must never be polluted by `Firing`.

---

## 3. Why order matters (failure mode)

If the server emits **VehicleFlags first, Firing second**:

1. Client reads byte #1 as Firing, byte #2 as driving flags (or the dual mis-map: fire bit lands
   where handbrake is expected — same class of swap).
2. In the observed bug class documented on AutoCore: packing **VehicleFlags first**
   mis-delivered **`Firing & 1` (front-fire)** into the client **Handbreak** path
   (`vehicle+0x61C`).
3. Downstream on the client:
   - **`VehicleAction_calcWheelTorque` @ `0x598040`** — if `entity+0x61c ≠ 0` and wheel is
     rear: drive torque `× DAT_00a0f298` (**0.5**). Rear traction cut.
   - **`VehicleEntity_PushDriveAxesToController` (`FUN_004FBC10`) @ `0x4FBC10`** —
     `ctrl+0x24 = entity+0x61c` (Havok handbrake input on the controller).

**Symptom:** path NPCs that are **firing while driving** appear to “drive with the brakes on”
(half rear drive torque + handbrake asserted), even when the server never set `Handbreak`.

**Fix class:** restore **Firing → VehicleFlags** order; keep `VehicleFlags` clear of
`Handbreak` for NPC cruise unless a true handbrake is intended.

---

## 4. PositionMask block layout (context)

Relevant portion of non-initial / initial pose when `PositionMask` is set
(`GhostVehicle` pack path):

| # | Field | Encoding |
|---|---|---|
| 1–3 | Position XYZ | `float` ×3 |
| 4–7 | Rotation XYZW | `float` ×4 |
| 8–10 | Velocity XYZ | `float` ×3 |
| 11–13 | AngularVelocity XYZ | `float` ×3 |
| **14** | **`Firing`** | **`byte`** |
| **15** | **`VehicleFlags`** | **`byte`** |
| 16 | Acceleration (throttle axis) | `WriteSignedFloat(..., 6)` |
| 17 | Steering | `WriteSignedFloat(..., 6)` |
| 18 | WantedTurretDirection | `float` |

Regression coverage walks this layout and asserts bytes 14–15 after the twelve pose floats.

---

## 5. Test lock

| Test | File | Asserts |
|---|---|---|
| `PackUpdate_NonInitial_PoseFlagBytes_FiringFirst_HandbrakeNotSetByFiring` | `src/AutoCore.Game.Tests/TNL/Ghost/GhostVehicleWireRegressionTests.cs` | `Firing=1`, `VehicleFlags=0` on wire; first flag byte == 1; second == 0; second `& 0x1 == 0` |
| `PackUpdate_NonInitial_PositionHealthTargetTokenShields` | same | Comment + read order: Firing first, then driving-flags byte |

Arrange for the handbrake regression:

```text
Firing       = 1   // front weapon on
VehicleFlags = 0   // Handbreak never set for NPC cruise
mask         = PositionMask
```

Expected wire after vel/angVel:

```text
byte0 = 0x01  // Firing
byte1 = 0x00  // VehicleFlags (Handbreak clear)
```

If pack order were reversed with the same field values, the client-side handbrake slot would see
`0x01` whenever the vehicle is front-firing.

---

## 6. NPC / server policy (related, not packing)

Separate from byte order, path / soft-drive code must not set `VehicleMovedFlags.Handbreak` to
encode “sharp turn”:

- Retail AI sets local `entity+0x61c` for sharp assist under speed/lateral gates, but the **ghost
  wire Handbreak bit** is not the right permanent cruise flag for NPCs.
- AutoCore path motion keeps `sharpTurn` off the `VehicleFlags` bit0 path; throttle/steer alone
  drive wheel visuals when `VehicleAction` exists.
- See also: `docs/NPC_DRIVING_FIX_RE.md` (“packing order already fixed: Firing then VehicleFlags”),
  `docs/NPCDriving.md` VehicleFlags/sharp row.

---

## 7. Do not confuse with `VehicleMovedPacket` (C2S)

`VehicleMovedPacket.Read` (player move report) uses a **different** field order after
base pose floats:

```text
Acceleration, Steering, TurretDirection,
VehicleFlags,   // first among the two flag bytes on THIS packet
Firing,         // second
unknown u16, Target TFID
```

That is **not** the ghost PositionMask layout. Ghost pack order is **Firing then VehicleFlags**.
Treat the two paths as independent wire formats.

---

## 8. Client anchors (handbrake sink)

| Addr | Symbol / role |
|---|---|
| `0x5F7720` | `VehicleNet_UnpackGhostVehicle` — pose unpack; flag-byte pair after angVel |
| `0x504C70` | `Vehicle_setDrivingInputs` — net entry after unpack (axes + pose apply) |
| `0x4FBC10` | `PushDriveAxesToController` — `entity+0x61c` → controller `+0x24` handbrake |
| `0x598040` | `VehicleAction_calcWheelTorque` — rear drive torque ×0.5 when `+0x61c ≠ 0` |
| `entity+0x61c` | Handbrake / sharp byte (u8); consumer of Handbreak wire bit |
| `DAT_00a0f298` | `0.5` rear handbrake drive-torque scale |

Decompile plate for push axes (`tmp/decompile_4fbc10.json` / verified brake notes):

```text
ctrl+0x24 = entity+0x61c   // handbrake byte
// entity+0x61c also read in calcWheelTorque for rear ×0.5
```

---

## 9. Checklist for future pack changes

1. After angVel, emit **`Firing` then `VehicleFlags`** — never reverse.
2. Keep regression `PackUpdate_NonInitial_PoseFlagBytes_FiringFirst_HandbrakeNotSetByFiring` green.
3. Do not set `Handbreak` on NPC cruise to mean “sharp”; leave bit0 clear unless true handbrake.
4. Do not copy order from `VehicleMovedPacket` C2S into ghost pack.
5. When debugging “NPCs drag while shooting,” inspect wire byte #2 for accidental `0x01` and pack
   order before tuning physics.

---

## 10. Related docs

| Doc | Relevance |
|---|---|
| `docs/reconstruction/physics/brake-spec.md` | Handbrake = rear drive cut, not service brake |
| `docs/reconstruction/physics/verified/fn_00598040_calcWheelTorque.md` | ×0.5 rear cut when `+0x61c` |
| `docs/reconstruction/physics/0.8-struct-offsets.md` | `+0x61c` / controller `+0x24` |
| `docs/NPCDriving.md` / `docs/NPC_DRIVING_FIX_RE.md` | NPC drive + wire policy |
| `src/AutoCore.Game/TNL/Ghost/GhostVehicle.cs` | Authoritative pack order + comments |
| `src/AutoCore.Game/Packets/Sector/VehicleMovedPacket.cs` | `VehicleMovedFlags` enum; C2S layout differs |
