"""Tests drive reconstructed_logic (extracted from reconstructed-exact) + structure-check C++."""
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import reconstructed_logic as rl


class TestReconCppNotStubs(unittest.TestCase):
    def test_respawn_cpp(self):
        rl.assert_recon_not_stub(
            "Client_SendRespawnInSector.cpp",
            ["0x2073", "0xe98", "0x250", "0xd28", "Client_SendSectorPacket", "0x28"],
        )

    def test_give_mission_cpp(self):
        rl.assert_recon_not_stub(
            "CVOGReaction_GiveMission.cpp",
            ["0x540", "0x548", "CNDHash_LookupByKey", "return 0", "return 1"],
        )

    def test_unequip_cpp(self):
        rl.assert_recon_not_stub(
            "Client_SendInventoryUnequip.cpp",
            ["0x203E", "0x30", "FUN_005714e0", "return 0", "return 1"],
        )

    def test_drop_hardpoint_cpp(self):
        rl.assert_recon_not_stub(
            "Client_SendInventoryDrop_Hardpoint.cpp",
            ["0x2036", "HARDPOINT", "pkt[0x1A] = 2"],
        )

    def test_equip_recv_cpp(self):
        rl.assert_recon_not_stub(
            "Client_RecvInventoryEquip.cpp",
            ["0x203C", "0x40", "putInHand", "case 10"],
        )

    def test_unlock_region_cpp(self):
        rl.assert_recon_not_stub(
            "Client_RecvUnlockRegion.cpp",
            ["unlockFlag", "RelockContinentObject", "area <= 32", "0x205B"],
        )

    def test_onkeydown_cpp(self):
        rl.assert_recon_not_stub(
            "Client_Input_OnKeyDown_MatchAction.cpp",
            ["0x2a", "held", "edge", "FUN_0093a5c0"],
        )

    def test_ghost_cpp(self):
        rl.assert_recon_not_stub(
            "VehicleNet_UnpackGhostVehicle.cpp",
            [
                "Ghost_ApplyCombatFields",
                "Ghost_ClampShield",
                "Ghost_UnpackOwnerForm",
                "Ghost_ReadOwnerBlockAndUnpack",
                "Ghost_ApplyDriveInputs",
                "FUN_005f5ad0",
                "Vehicle_setDrivingInputs",
            ],
        )
        # Mechanical wiring: owner helper must be called from entry path
        rl.assert_recon_call_site(
            "VehicleNet_UnpackGhostVehicle.cpp", "Ghost_UnpackOwnerForm"
        )
        rl.assert_recon_call_site(
            "VehicleNet_UnpackGhostVehicle.cpp", "Ghost_ReadOwnerBlockAndUnpack"
        )
        text = rl.read_recon_cpp("VehicleNet_UnpackGhostVehicle.cpp")
        self.assertNotIn("(void)hasOwner", text)
        self.assertIn("bool shieldDirty, int shield", text)
        self.assertIn("bool shieldMaxDirty, int shieldMax", text)
        self.assertIn("FUN_005f5ad0(1, 1)", text)
        self.assertIn("FUN_005f5ad0(1, 0)", text)
        compact = text.replace(" ", "").replace("\n", "")
        self.assertIn("*(int*)((char*)vehicle+0x150)", compact)
        self.assertIn("*(int*)((char*)vehicle+0x144)", compact)


class TestPacketLayoutsFromRecon(unittest.TestCase):
    def test_respawn_packet(self):
        raw = rl.pack_respawn_in_sector((1.0, 2.0, 3.0), (0, 0, 0, 1.0), 0x1122334455667788)
        self.assertEqual(len(raw), 0x28)
        self.assertEqual(struct.unpack_from("<I", raw, 0)[0], 0x2073)

    def test_use_object_packet(self):
        tfid = bytes(range(16))
        raw = rl.pack_use_object(tfid, -1)
        self.assertEqual(len(raw), 0x20)
        self.assertEqual(struct.unpack_from("<I", raw, 0)[0], 0x2072)

    def test_unequip_packet(self):
        raw = rl.pack_inventory_unequip(0x111, 0x222, 3, 4)
        self.assertEqual(len(raw), 0x30)
        self.assertEqual(struct.unpack_from("<I", raw, 0)[0], 0x203E)

    def test_drop_hardpoint_packet(self):
        raw = rl.pack_inventory_drop_hardpoint(10, 20, 30)
        self.assertEqual(len(raw), 0x20)
        self.assertEqual(raw[0x1A], 2)

    def test_grab_packet(self):
        raw = rl.pack_inventory_grab(0x10, 0x20, 3, 5)
        self.assertEqual(len(raw), 0x20)


class TestBranchLogicFromRecon(unittest.TestCase):
    def test_pose_threshold(self):
        self.assertFalse(rl.pose_requires_hard_teleport((0, 0, 0), (15, 0, 0)))
        self.assertTrue(rl.pose_requires_hard_teleport((0, 0, 0), (15.1, 0, 0)))

    def test_throttle_clamp(self):
        self.assertEqual(rl.clamp_throttle(1.0, True), 0.9)

    def test_quickbar_index(self):
        self.assertEqual(rl.quickbar_index(3, 2), 23)

    def test_secondary_fire_mask(self):
        self.assertTrue(rl.secondary_fire_flags_block(0xD2))
        self.assertFalse(rl.secondary_fire_flags_block(0x01))

    def test_special_event_tfid(self):
        self.assertTrue(rl.special_event_tfid_matches(1, 2, 1, 2))

    def test_give_mission_gates(self):
        self.assertTrue(rl.give_mission_should_reject_disabled(False))
        self.assertTrue(rl.give_mission_prereq_blocks(1, False, True, False))

    def test_unlock_region_bits(self):
        self.assertTrue(rl.unlock_region_is_relock(0))
        self.assertEqual(rl.explored_bits_to_set(0, 0b101), [1, 3])

    def test_shift_dik(self):
        self.assertEqual(rl.action_table_shift_dik_when_any_shift(True), 0x2A)

    def test_ghost_apply_order_matches_raw(self):
        """Skeptic dual-dirty: cur 40/50 + shield 80 + max 100 → shield ends 50 not 80."""
        h, mx, sh = rl.ghost_apply_combat_fields(
            heat_dirty=False,
            heat=0,
            shield_dirty=True,
            shield=80,
            shield_max_dirty=True,
            shield_max=100,
            cur_shield=40,
            cur_max=50,
            cur_heat=0,
        )
        self.assertEqual(mx, 100)
        self.assertEqual(sh, 50)  # raw order: clamp vs old max 50 first
        # Opposite wrong-order bug would yield 80

        # max drop after shield: cur 50/100, shield dirty 80, max dirty 30 → 30
        h2, mx2, sh2 = rl.ghost_apply_combat_fields(
            False, 0, True, 80, True, 30, cur_shield=50, cur_max=100, cur_heat=7
        )
        self.assertEqual(h2, 7)
        self.assertEqual(mx2, 30)
        self.assertEqual(sh2, 30)

        # shield only
        self.assertEqual(rl.ghost_clamp_shield(50, 100), 50)
        self.assertEqual(rl.ghost_clamp_shield(150, 100), 100)
        self.assertEqual(rl.ghost_clamp_shield(-5, 100), 0)

        # clean form must document apply order heat then shield then max
        text = rl.read_recon_cpp("VehicleNet_UnpackGhostVehicle.cpp")
        # heat block before shield block before shieldMax in Apply function
        i_heat = text.find("if (heatDirty)")
        i_shield = text.find("if (shieldDirty)")
        i_max = text.find("if (shieldMaxDirty)")
        self.assertTrue(0 <= i_heat < i_shield < i_max)

    def test_ghost_owner_form_args(self):
        self.assertEqual(rl.ghost_owner_form_call(False), (1, 1))
        self.assertEqual(rl.ghost_owner_form_call(True), (1, 0))
        text = rl.read_recon_cpp("VehicleNet_UnpackGhostVehicle.cpp")
        self.assertIn("Ghost_UnpackOwnerForm", text)
        self.assertIn("Ghost_ApplyDriveInputs", text)

    def test_inc_option_dispatch(self):
        self.assertEqual(rl.inc_option_on_countdown_zero(0), 0)
        self.assertTrue(rl.inc_option_airlift_sends_respawn(0))
        rl.assert_recon_not_stub(
            "Client_INC_ContactCountdownTick.cpp",
            ["Client_SendRespawnInSector", "0xc30", "kIncOptionAirlift"],
        )

    def test_packet_dispatch_map(self):
        self.assertTrue(rl.packet_dispatch_mission_opcodes_not_swapped())
        self.assertEqual(rl.packet_dispatch_handler_name(0x20A9), "Client_RecvSpecialEvent")
        self.assertEqual(rl.packet_dispatch_handler_name(0x205F), "Client_AwardKillExperience")

    def test_give_xp_path(self):
        self.assertTrue(rl.give_xp_should_bail_no_character(False))
        self.assertTrue(rl.give_xp_should_enqueue_floater(True))
        self.assertEqual(rl.give_xp_floater_type(), 3)
        buf = rl.give_xp_build_floater_buffer(
            (1, 2, 3, 4), (10, 11, 12, 13), 999
        )
        self.assertEqual(len(buf), rl.GIVE_XP_FLOATER_SIZE)  # 0x34
        self.assertEqual(len(buf), 0x34)
        # type 3 at exact offsetof 0x30 — not merely last u32 theater
        self.assertEqual(struct.unpack_from("<I", buf, 0x30)[0], 3)
        self.assertEqual(struct.unpack_from("<I", buf, 0x20)[0], 999)  # amount
        self.assertEqual(buf[0x28], 0)  # flag
        text = rl.read_recon_cpp("Client_AwardKillExperience.cpp")
        self.assertIn("uStack_8 = 3", text)
        self.assertIn("offsetof(GiveXpFloaterStack, uStack_8) == 0x30", text)
        self.assertIn("sizeof(GiveXpFloaterStack) == 0x34", text)
        rl.assert_recon_call_site(
            "Client_AwardKillExperience.cpp", "Client_EnqueueCombatFloater_INFERRED"
        )
        self.assertIn("Client_EnqueueCombatFloater_INFERRED(&frame)", text)
        self.assertNotIn("(void)uStack_8", text)
        rl.assert_recon_not_stub(
            "Client_AwardKillExperience.cpp",
            ["GiveXpFloaterStack", "uStack_8 = 3", "0x164", "0x250", "static_assert"],
        )

    def test_npc_mission_dialog(self):
        rl.assert_recon_not_stub(
            "Client_RecvNpcMissionDialog.cpp",
            ["0x206D", "Client_ShowNpcMissionDialogUI"],
        )


if __name__ == "__main__":
    unittest.main()
