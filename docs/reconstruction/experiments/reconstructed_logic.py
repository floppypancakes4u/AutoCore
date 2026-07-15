"""
Pure logic extracted from reconstructed-exact/*.cpp (must stay in sync with raw apply order).
"""
from __future__ import annotations

import struct
from pathlib import Path

RECON_DIR = Path(__file__).resolve().parents[1] / "reconstructed-exact"

# --- opcodes / sizes ---
OPCODE_RESPAWN_IN_SECTOR = 0x2073
OPCODE_USE_OBJECT = 0x2072
OPCODE_INVENTORY_GRAB = 0x2034
OPCODE_INVENTORY_EQUIP = 0x203C
OPCODE_INVENTORY_UNEQUIP = 0x203E
OPCODE_INVENTORY_DROP = 0x2036
OPCODE_SPECIAL_EVENT = 0x20A9
OPCODE_UNLOCK_REGION = 0x205B

RESPAWN_PACKET_SIZE = 0x28
USE_OBJECT_PACKET_SIZE = 0x20
INVENTORY_GRAB_PACKET_SIZE = 0x20
INVENTORY_EQUIP_SIZE = 0x40
INVENTORY_UNEQUIP_SIZE = 0x30
INVENTORY_DROP_SIZE = 0x20
INVENTORY_DROP_TYPE_HARDPOINT = 2

POSE_SOFT_TELEPORT_DISTANCE = 15.0
THROTTLE_CLAMP_SOFT = 0.9

HEAT_MASK = 0x20000000
SHIELD_MASK = 0x04000000
POWER_MASK = 0x08000000
SHIELD_MAX_MASK = 0x02000000

VEHICLE_OFF_SHIELD = 0x144
VEHICLE_OFF_MAX_SHIELD = 0x148
VEHICLE_OFF_HEAT = 0x150


def read_recon_cpp(name: str) -> str:
    path = RECON_DIR / name
    if not path.exists():
        raise FileNotFoundError(path)
    return path.read_text(encoding="utf-8")


def assert_recon_not_stub(name: str, required_snippets: list[str]) -> None:
    text = read_recon_cpp(name)
    non_comment = [
        ln
        for ln in text.splitlines()
        if ln.strip()
        and not ln.strip().startswith("/*")
        and not ln.strip().startswith("*")
        and not ln.strip().startswith("//")
        and not ln.strip().startswith("*/")
    ]
    if len(non_comment) < 8:
        raise AssertionError(f"{name} looks like a stub ({len(non_comment)} code lines)")
    for snip in required_snippets:
        if snip not in text:
            raise AssertionError(f"{name} missing required snippet: {snip!r}")


def _strip_c_comments(text: str) -> str:
    """Remove // line comments and /* */ blocks for call-site scanning."""
    import re

    no_block = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    lines = []
    for ln in no_block.splitlines():
        if "//" in ln:
            ln = ln[: ln.index("//")]
        lines.append(ln)
    return "\n".join(lines)


def assert_recon_call_site(name: str, fn: str, *, min_calls: int = 1) -> None:
    """Fail if fn( appears only in comments/definitions or not at all in executable text.

    Counts non-comment occurrences of ``fn(``. For helpers that are both defined
    and called, requires at least one call site outside the definition line
    ``void fn(`` / ``inline void fn(`` / ``bool fn(``.
    """
    text = read_recon_cpp(name)
    code = _strip_c_comments(text)
    needle = f"{fn}("
    # Collect lines with calls
    call_lines = []
    for ln in code.splitlines():
        if needle not in ln:
            continue
        stripped = ln.strip()
        # Skip pure declarations/definitions of the function itself
        if stripped.startswith("inline ") and f"{fn}(" in stripped and "{" not in stripped:
            # could still be a forward decl
            if stripped.endswith(";") or stripped.endswith(")"):
                # definition start may continue next lines — allow body calls only
                if any(
                    stripped.startswith(p)
                    for p in (
                        f"inline void {fn}(",
                        f"inline bool {fn}(",
                        f"void {fn}(",
                        f"bool {fn}(",
                        f"inline int {fn}(",
                    )
                ):
                    continue
        if any(
            stripped.startswith(p)
            for p in (
                f"inline void {fn}(",
                f"inline bool {fn}(",
                f"void {fn}(",
                f"bool {fn}(",
                f"inline int {fn}(",
            )
        ):
            continue
        call_lines.append(stripped)
    if len(call_lines) < min_calls:
        raise AssertionError(
            f"{name}: expected ≥{min_calls} live call site(s) of {fn}(, found {len(call_lines)}: {call_lines}"
        )


def pack_respawn_in_sector(
    pos: tuple[float, float, float],
    quat: tuple[float, float, float, float],
    coid: int,
) -> bytes:
    lo = coid & 0xFFFFFFFF
    hi = (coid >> 32) & 0xFFFFFFFF
    raw = struct.pack("<I", OPCODE_RESPAWN_IN_SECTOR)
    raw += struct.pack("<3f", *pos)
    raw += struct.pack("<4f", *quat)
    raw += struct.pack("<II", lo & 0xFFFFFFFF, hi & 0xFFFFFFFF)
    assert len(raw) == RESPAWN_PACKET_SIZE
    return raw


def pack_use_object(tfid: bytes, objective_id: int) -> bytes:
    if len(tfid) != 16:
        raise ValueError("tfid must be 16 bytes")
    body = struct.pack("<II", OPCODE_USE_OBJECT, 0) + tfid + struct.pack("<i", objective_id)
    return body + b"\x00" * (USE_OBJECT_PACKET_SIZE - len(body))


def pack_inventory_grab(item_lo: int, item_hi: int, inv_type: int, quantity: int) -> bytes:
    buf = bytearray(INVENTORY_GRAB_PACKET_SIZE)
    struct.pack_into("<I", buf, 0, OPCODE_INVENTORY_GRAB)
    struct.pack_into("<i", buf, 8, item_lo)
    struct.pack_into("<i", buf, 12, item_hi)
    buf[0x10] = inv_type & 0xFF
    struct.pack_into("<i", buf, 0x1C, quantity)
    return bytes(buf)


def pack_inventory_unequip(item_lo: int, item_hi: int, dest_x: int, dest_y: int) -> bytes:
    buf = bytearray(INVENTORY_UNEQUIP_SIZE)
    struct.pack_into("<I", buf, 0, OPCODE_INVENTORY_UNEQUIP)
    struct.pack_into("<i", buf, 8, item_lo)
    struct.pack_into("<i", buf, 12, item_hi)
    buf[0x28] = dest_x & 0xFF
    buf[0x29] = dest_y & 0xFF
    return bytes(buf)


def pack_inventory_drop_hardpoint(item_a: int, item_b: int, item_c: int) -> bytes:
    buf = bytearray(INVENTORY_DROP_SIZE)
    struct.pack_into("<I", buf, 0, OPCODE_INVENTORY_DROP)
    struct.pack_into("<i", buf, 8, item_a)
    struct.pack_into("<i", buf, 12, item_b)
    buf[0x10] = item_c & 0xFF
    buf[0x18] = 0xFF
    buf[0x19] = 0xFF
    buf[0x1A] = INVENTORY_DROP_TYPE_HARDPOINT
    return bytes(buf)


def pose_requires_hard_teleport(
    current: tuple[float, float, float], target: tuple[float, float, float]
) -> bool:
    dx = target[0] - current[0]
    dy = target[1] - current[1]
    dz = target[2] - current[2]
    return (dx * dx + dy * dy + dz * dz) ** 0.5 > POSE_SOFT_TELEPORT_DISTANCE


def clamp_throttle(value: float, soft_limit_enabled: bool) -> float:
    if soft_limit_enabled and value >= THROTTLE_CLAMP_SOFT:
        return THROTTLE_CLAMP_SOFT
    return value


def quickbar_index(slot: int, page: int) -> int:
    if not (0 <= slot <= 9):
        raise ValueError("slot out of range")
    if page < 0:
        raise ValueError("page must be resolved non-negative")
    return slot + page * 10


def special_event_tfid_matches(local_lo: int, local_hi: int, pkt_lo: int, pkt_hi: int) -> bool:
    return local_lo == pkt_lo and local_hi == pkt_hi


def secondary_fire_flags_block(flags_b8: int) -> bool:
    return (flags_b8 & 0xD2) != 0


def give_mission_should_reject_disabled(template_enabled: bool) -> bool:
    return not template_enabled


def give_mission_prereq_blocks(
    short_2b: int, allow_bonus: bool, in_completed_538: bool, in_completed_53c: bool
) -> bool:
    if short_2b == -1:
        return False
    if not allow_bonus and in_completed_538:
        return True
    if allow_bonus and in_completed_53c:
        return True
    return False


def unlock_region_is_relock(unlock_flag: int) -> bool:
    return unlock_flag == 0


def explored_bits_to_set(prev: int, new: int) -> list[int]:
    out = []
    for area in range(1, 33):
        mask = 1 << (area - 1)
        if (new & mask) and not (prev & mask):
            out.append(area)
    return out


def action_table_shift_dik_when_any_shift(shift_down: bool) -> int:
    return 0x2A if shift_down else 0


INC_OPTION_AIRLIFT = 0
INC_OPTION_INSTANT_REPAIR = 1
INC_OPTION_TRANSFER = 2


def inc_option_on_countdown_zero(option: int) -> int:
    if option in (0, 1, 2):
        return option
    return -1


def inc_option_airlift_sends_respawn(option: int) -> bool:
    return option == INC_OPTION_AIRLIFT


def give_xp_should_bail_no_character(has_character: bool) -> bool:
    return not has_character


def give_xp_should_apply_level_hint(hint: int) -> bool:
    return hint != -1


def give_xp_should_enqueue_floater(has_vehicle: bool) -> bool:
    return has_vehicle


def give_xp_floater_type() -> int:
    return 3


# GiveXpFloaterStack layout (mirrors reconstructed-exact static_assert):
# uStack_18 @ +0x20, uStack_10 @ +0x28, uStack_8 @ +0x30, sizeof 0x34
GIVE_XP_FLOATER_SIZE = 0x34
GIVE_XP_FLOATER_TYPE_OFF = 0x30
GIVE_XP_FLOATER_AMOUNT_OFF = 0x20
GIVE_XP_FLOATER_FLAG_OFF = 0x28


def give_xp_build_floater_buffer(
    colors: tuple[int, int, int, int],
    tfid: tuple[int, int, int, int],
    amount: int,
) -> bytes:
    """Byte image of GiveXpFloaterStack: type 3 at +0x30, size 0x34."""
    buf = bytearray(GIVE_XP_FLOATER_SIZE)
    for i, c in enumerate(colors):
        struct.pack_into("<I", buf, i * 4, c & 0xFFFFFFFF)
    for i, t in enumerate(tfid):
        struct.pack_into("<I", buf, 0x10 + i * 4, t & 0xFFFFFFFF)
    struct.pack_into("<I", buf, GIVE_XP_FLOATER_AMOUNT_OFF, amount & 0xFFFFFFFF)
    # hole_24 at +0x24 already zero
    buf[GIVE_XP_FLOATER_FLAG_OFF] = 0  # uStack_10
    # pad_29[7] zero → uStack_8 at +0x30
    struct.pack_into("<I", buf, GIVE_XP_FLOATER_TYPE_OFF, 3)
    return bytes(buf)


def ghost_clamp_shield(shield: int, max_shield: int) -> int:
    """Raw local_f5 clamp against CURRENT max."""
    v = shield
    if max_shield <= shield:
        v = max_shield
    if v < 1:
        return 0
    if shield < max_shield:
        return shield
    return max_shield


def ghost_apply_combat_fields(
    heat_dirty: bool,
    heat: int,
    shield_dirty: bool,
    shield: int,
    shield_max_dirty: bool,
    shield_max: int,
    cur_shield: int = 0,
    cur_max: int = 0,
    cur_heat: int = 0,
) -> tuple[int, int, int]:
    """RAW apply order: heat → shield (vs current max) → shieldMax.

    Returns (heat, max_shield, current_shield).
    """
    h, mx, sh = cur_heat, cur_max, cur_shield
    # 1) heat
    if heat_dirty:
        h = heat
    # 2) shield against CURRENT max
    if shield_dirty:
        sh = ghost_clamp_shield(shield, mx)
    # 3) shieldMax then maybe clamp current
    if shield_max_dirty:
        mx = shield_max
        if mx < sh:
            sh = mx
    return h, mx, sh


def bitstream_flag_byte(buffer: bytes, bit_pos: int) -> bool:
    return (buffer[bit_pos >> 3] & (1 << (bit_pos & 7))) != 0


def ghost_owner_form_call(vehicle_owner_form: bool) -> tuple[int, int]:
    """Returns FUN_005f5ad0 args: (1,1) creature / (1,0) vehicle."""
    return (1, 0) if vehicle_owner_form else (1, 1)


_PACKET_HANDLERS = {
    0x2017: "Client_RecvCharacterLevel",
    0x201D: "Client_CreateVehicleObjectApply_0",
    0x201E: "Client_CreateVehicleObjectApply_1",
    0x2020: "Client_RecvDestroyObject",
    0x2035: "Client_RecvInventoryGrabResponse",
    0x2039: "Client_RecvInventoryGrabResponse",
    0x2037: "Client_RecvInventoryDropResponse",
    0x203B: "Client_RecvInventoryDropResponse",
    0x203C: "Client_RecvInventoryEquip",
    0x203E: "Client_RecvInventoryUnequipNotify",
    0x203F: "Client_RecvInventoryUnequipResponse",
    0x2044: "Client_RecvInventoryUsePaint",
    0x2046: "Client_RecvInventoryUseItemResponse",
    0x2047: "Client_RecvInventoryAddItem",
    0x205B: "Client_RecvUnlockRegion",
    0x205E: "Client_RecvGiveCredits",
    0x205F: "Client_AwardKillExperience",
    0x206C: "Client_RecvGroupReactionCall",
    0x206D: "Client_RecvNpcMissionDialog",
    0x2070: "Client_RecvCompleteDynamicObjective",
    0x2071: "Client_RecvObjectiveState",
    0x20A9: "Client_RecvSpecialEvent",
}


def packet_dispatch_handler_name(opcode: int) -> str | None:
    return _PACKET_HANDLERS.get(opcode)


def packet_dispatch_mission_opcodes_not_swapped() -> bool:
    return (
        packet_dispatch_handler_name(0x2070) == "Client_RecvCompleteDynamicObjective"
        and packet_dispatch_handler_name(0x2071) == "Client_RecvObjectiveState"
    )
