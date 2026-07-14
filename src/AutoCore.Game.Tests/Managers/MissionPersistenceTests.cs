using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.Char.Models;
using System.ComponentModel.DataAnnotations.Schema;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Mission-state persistence: latest-wins queue, enqueue→flush, login load, completed-mission
/// re-grant guard, and completed-id create-packet delivery.
/// </summary>
[TestClass]
public class MissionPersistenceTests
{
    [TestInitialize]
    public void SetUp()
    {
        MissionPersistence.Instance.ResetPersistenceForTests();
        AssetManager.Instance.ClearTestMissions();
    }

    [TestCleanup]
    public void TearDown()
    {
        MissionPersistence.Instance.ResetPersistenceForTests();
        AssetManager.Instance.ClearTestMissions();
    }

    // ---- Queue ----

    [TestMethod]
    public void PersistenceModels_MapToCanonicalMissionTables()
    {
        Assert.AreEqual("character_mission",
            typeof(CharacterQuestData).GetCustomAttributes(typeof(TableAttribute), false)
                .Cast<TableAttribute>().Single().Name);
        Assert.AreEqual("character_mission_completed",
            typeof(CharacterCompletedMissionData).GetCustomAttributes(typeof(TableAttribute), false)
                .Cast<TableAttribute>().Single().Name);
    }

    [TestMethod]
    public void Queue_Enqueue_LatestWinsPerKey()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(10, 100, QuestPersistOp.Upsert(0, 0, System.Array.Empty<byte>()));
        queue.Enqueue(10, 100, QuestPersistOp.Complete()); // supersedes the upsert
        queue.Enqueue(10, 200, QuestPersistOp.Upsert(3, 0, System.Array.Empty<byte>()));

        Assert.AreEqual(2, queue.PendingCount);

        var ops = new List<(long Coid, int MissionId, QuestPersistKind Kind)>();
        var count = queue.Flush((coid, missionId, op) => ops.Add((coid, missionId, op.Kind)));

        Assert.AreEqual(2, count);
        Assert.AreEqual(0, queue.PendingCount);
        CollectionAssert.Contains(ops, (10L, 100, QuestPersistKind.Complete));
        CollectionAssert.Contains(ops, (10L, 200, QuestPersistKind.Upsert));
    }

    [TestMethod]
    public void Queue_Flush_Empty_ReturnsZero_AndNullThrows()
    {
        var queue = new MissionPersistenceQueue();
        Assert.AreEqual(0, queue.Flush((_, _, _) => Assert.Fail("no entries")));
        Assert.ThrowsException<System.ArgumentNullException>(() => queue.Flush(null));
    }

    [TestMethod]
    public void Queue_Clear_DropsPending()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(1, 2, QuestPersistOp.Complete());
        queue.Clear();
        Assert.AreEqual(0, queue.PendingCount);
    }

    [TestMethod]
    public void Queue_RemoveForCharacter_DropsOnlyThatCharacter()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(10, 1, QuestPersistOp.Complete());
        queue.Enqueue(10, 2, QuestPersistOp.Complete());
        queue.Enqueue(11, 1, QuestPersistOp.Complete());

        queue.RemoveForCharacter(10);

        Assert.AreEqual(1, queue.PendingCount);
        var remaining = new List<long>();
        queue.Flush((coid, _, _) => remaining.Add(coid));
        CollectionAssert.AreEqual(new[] { 11L }, remaining);
    }

    [TestMethod]
    public void Queue_RemoveUpsertsForCharacter_LeavesCompleteOps()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(10, 100, QuestPersistOp.Upsert(0, 0, Array.Empty<byte>()));
        queue.Enqueue(10, 200, QuestPersistOp.Complete());
        queue.Enqueue(11, 100, QuestPersistOp.Upsert(1, 0, Array.Empty<byte>()));

        queue.RemoveUpsertsForCharacter(10);

        Assert.AreEqual(2, queue.PendingCount);
        var ops = new List<(long Coid, int MissionId, QuestPersistKind Kind)>();
        queue.Flush((coid, missionId, op) => ops.Add((coid, missionId, op.Kind)));

        CollectionAssert.Contains(ops, (10L, 200, QuestPersistKind.Complete));
        CollectionAssert.Contains(ops, (11L, 100, QuestPersistKind.Upsert));
        CollectionAssert.DoesNotContain(ops, (10L, 100, QuestPersistKind.Upsert));
    }

    [TestMethod]
    public void Queue_FailedPersist_RemainsPendingForRetry()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(10, 100, QuestPersistOp.Upsert(0, 0, Array.Empty<byte>()));

        var first = queue.Flush((_, _, _) => throw new InvalidOperationException("database unavailable"));

        Assert.AreEqual(0, first);
        Assert.AreEqual(1, queue.PendingCount);

        var writes = 0;
        var second = queue.Flush((_, _, _) => writes++);
        Assert.AreEqual(1, second);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(0, queue.PendingCount);
    }

    [TestMethod]
    public void MissionCompatibilityMigration_UsesCompletedWinsAndPreservesCanonicalActiveRows()
    {
        var sql = string.Join("\n", typeof(AutoCore.Database.Char.CharContext)
            .GetField("MissionCompatibilityMigrationSql",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as string[] ?? Array.Empty<string>());

        StringAssert.Contains(sql, "INSERT IGNORE INTO `character_mission_completed`");
        StringAssert.Contains(sql, "FROM `character_completed_mission`");
        StringAssert.Contains(sql, "INSERT IGNORE INTO `character_mission`");
        StringAssert.Contains(sql, "FROM `character_quest`");
        StringAssert.Contains(sql, "DELETE active");
        StringAssert.Contains(sql, "INNER JOIN `character_mission_completed`");
    }

    [TestMethod]
    public void Manager_DeleteAllForCharacter_ClearsQueueAndInvokesDelete()
    {
        var manager = MissionPersistence.Instance;
        long? deleted = null;
        manager.DeleteAllRows = coid => deleted = coid;

        manager.OnMissionCompleted(30, 1);   // pending op for coid 30
        manager.OnMissionCompleted(31, 2);   // pending op for coid 31
        manager.DeleteAllForCharacter(30);

        Assert.AreEqual(30L, deleted);
        Assert.AreEqual(1, manager.PendingPersistCount, "only coid 30's pending op is dropped");
    }

    // ---- Progress packing ----

    [TestMethod]
    public void PackUnpack_RoundTripsProgressSlots()
    {
        var progress = new[] { 0, 3, 7, 0, 42 };
        var packed = MissionPersistence.PackProgress(progress);
        Assert.AreEqual(progress.Length * sizeof(int), packed.Length);

        var restored = MissionPersistence.UnpackProgress(packed);
        CollectionAssert.AreEqual(progress, restored);
    }

    [TestMethod]
    public void PackUnpack_NullAndEmpty_AreSafe()
    {
        Assert.AreEqual(0, MissionPersistence.PackProgress(null).Length);
        Assert.AreEqual(0, MissionPersistence.PackProgress(System.Array.Empty<int>()).Length);
        Assert.AreEqual(0, MissionPersistence.UnpackProgress(null).Length);
        Assert.AreEqual(0, MissionPersistence.UnpackProgress(new byte[] { 1, 2 }).Length); // < 4 bytes
    }

    // ---- Manager enqueue → flush ----

    [TestMethod]
    public void NpcAcceptance_EnqueuesCanonicalActiveMissionSnapshot()
    {
        SeedMission(554, repeatable: 0, (5540, 0, 1));
        var manager = MissionPersistence.Instance;
        var writes = new List<(long Coid, int MissionId, QuestPersistKind Kind)>();
        manager.PersistQuestRow = (coid, missionId, op) => writes.Add((coid, missionId, op.Kind));

        var character = new Character();
        character.SetCoid(7300, true);
        var connection = new TNLConnection();
        TNLConnection.TestPacketSink = (_, _) => { };
        try
        {
            NpcInteractHandler.GrantMission(connection, character, 554);
            Assert.AreEqual(1, manager.FlushPending());
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
        }

        Assert.AreEqual(1, character.CurrentQuests.Count);
        CollectionAssert.AreEqual(
            new[] { (7300L, 554, QuestPersistKind.Upsert) },
            writes);
    }

    [TestMethod]
    public void Manager_OnQuestChanged_EnqueuesUpsert_FlushInvokesPersist()
    {
        var manager = MissionPersistence.Instance;
        var writes = new List<(long Coid, int MissionId, QuestPersistKind Kind, byte Seq)>();
        manager.PersistQuestRow = (coid, missionId, op) => writes.Add((coid, missionId, op.Kind, op.ActiveObjectiveSequence));

        var character = new Character();
        character.SetCoid(4001, true);
        var quest = new CharacterQuest(555, 2);
        quest.ObjectiveProgress[0] = 4;

        manager.OnQuestChanged(character, quest);
        manager.OnQuestChanged(character, quest); // latest-wins: still one pending
        Assert.AreEqual(1, manager.PendingPersistCount);

        Assert.AreEqual(1, manager.FlushPending());
        Assert.AreEqual(0, manager.PendingPersistCount);
        Assert.AreEqual(1, writes.Count);
        Assert.AreEqual((4001L, 555, QuestPersistKind.Upsert, (byte)2), writes[0]);
    }

    [TestMethod]
    public void Manager_OnMissionCompleted_EnqueuesComplete()
    {
        var manager = MissionPersistence.Instance;
        QuestPersistKind? seen = null;
        manager.PersistQuestRow = (_, _, op) => seen = op.Kind;

        manager.OnMissionCompleted(4002, 777);
        Assert.AreEqual(1, manager.PendingPersistCount);
        manager.FlushPending();
        Assert.AreEqual(QuestPersistKind.Complete, seen);
    }

    [TestMethod]
    public void Manager_OnMissionFailed_EnqueuesRemove()
    {
        var manager = MissionPersistence.Instance;
        QuestPersistKind? seen = null;
        manager.PersistQuestRow = (_, _, op) => seen = op.Kind;

        manager.OnMissionFailed(4003, 888);
        Assert.AreEqual(1, manager.PendingPersistCount);
        manager.FlushPending();
        Assert.AreEqual(QuestPersistKind.Remove, seen);
    }

    [TestMethod]
    public void Queue_Remove_LatestWinsOverUpsert()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(10, 100, QuestPersistOp.Upsert(0, 0, Array.Empty<byte>()));
        queue.Enqueue(10, 100, QuestPersistOp.Remove());

        var ops = new List<QuestPersistKind>();
        queue.Flush((_, _, op) => ops.Add(op.Kind));

        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(QuestPersistKind.Remove, ops[0]);
    }

    [TestMethod]
    public void Manager_OnQuestChanged_NullArgs_NoEnqueue()
    {
        var manager = MissionPersistence.Instance;
        manager.OnQuestChanged(null, null);
        manager.OnQuestChanged(new Character(), null);
        Assert.AreEqual(0, manager.PendingPersistCount);
    }

    // ---- Login load ----

    [TestMethod]
    public void LoadMissions_RestoresActiveProgressAndCompleted()
    {
        // Mission 900: objective seq 0 requires 5, seq 1 requires 3.
        var o0 = MissionObjective.CreateForTests(9000, 0, 900, 5);
        var o1 = MissionObjective.CreateForTests(9001, 1, 900, 3);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(900, o0, o1));

        var character = new Character();
        character.SetCoid(5001, true);

        var questRow = new CharacterQuestData
        {
            CharacterCoid = 5001,
            MissionId = 900,
            ActiveObjectiveSequence = 1,
            State = 0,
            ObjectiveProgress = MissionPersistence.PackProgress(new[] { 5, 2 }),
        };
        var completed = new[] { new CharacterCompletedMissionData { CharacterCoid = 5001, MissionId = 42 } };

        character.SetMissionsForTests(new[] { questRow }, completed);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        var quest = character.CurrentQuests[0];
        Assert.AreEqual(900, quest.MissionId);
        Assert.AreEqual(1, quest.ActiveObjectiveSequence);
        Assert.AreEqual(5, quest.ObjectiveProgress[0]);
        Assert.AreEqual(2, quest.ObjectiveProgress[1]);
        Assert.AreEqual(5, quest.ObjectiveMax[0]); // re-derived from template CompleteCount
        Assert.AreEqual(3, quest.ObjectiveMax[1]);

        Assert.IsTrue(character.CompletedMissionIds.Contains(42));
    }

    [TestMethod]
    public void LoadMissions_EmptyRows_ClearsState()
    {
        var character = new Character();
        character.SetCoid(5002, true);
        character.CurrentQuests.Add(new CharacterQuest(1, 0));
        character.CompletedMissionIds.Add(1);

        character.SetMissionsForTests(null, null);

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.AreEqual(0, character.CompletedMissionIds.Count);
    }

    // ---- Re-grant guard (core requirement) ----

    [TestMethod]
    public void ReactionGiveMission_CompletedNonRepeatable_NotReGrantedAndNotSentToClient()
    {
        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
        SectorMap.SendGroupReactionCall = true;
        try
        {
            SeedMission(600, repeatable: 0, (6000, 0, 1));
            var (character, map) = CreatePlayer(continentId: 707);
            character.CompletedMissionIds.Add(600);

            PlaceGiveMission(map, coid: 61000, missionId: 600);
            map.TriggerReactions(character, new List<long> { 61000 });

            Assert.AreEqual(0, character.CurrentQuests.Count, "completed non-repeatable must not be re-granted");
            // The real bug: a declined GiveMission must NOT be broadcast as 0x206C, or the client re-adds it.
            Assert.AreEqual(0, sent.OfType<GroupReactionCallPacket>().Count(p => p.Count > 0),
                "declined GiveMission must not reach the client");
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
            SectorMap.SendGroupReactionCall = true;
        }
    }

    [TestMethod]
    public void ReactionGiveMission_ActiveMission_NotReSentToClient()
    {
        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
        SectorMap.SendGroupReactionCall = true;
        try
        {
            SeedMission(602, repeatable: 0, (6020, 0, 1));
            var (character, map) = CreatePlayer(continentId: 707);
            var quest = new CharacterQuest(602, 0);
            quest.PopulateFromAssets();
            character.CurrentQuests.Add(quest); // already active (e.g. restored from DB)

            PlaceGiveMission(map, coid: 61002, missionId: 602);
            map.TriggerReactions(character, new List<long> { 61002 });

            Assert.AreEqual(1, character.CurrentQuests.Count, "no duplicate quest");
            Assert.AreEqual(0, sent.OfType<GroupReactionCallPacket>().Count(p => p.Count > 0),
                "already-active GiveMission must not re-broadcast to the client");
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
            SectorMap.SendGroupReactionCall = true;
        }
    }

    [TestMethod]
    public void ReactionGiveMission_CompletedRepeatable_IsReGrantedAndSent()
    {
        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
        SectorMap.SendGroupReactionCall = true;
        try
        {
            SeedMission(601, repeatable: 1, (6010, 0, 1));
            var (character, map) = CreatePlayer(continentId: 707);
            character.CompletedMissionIds.Add(601);

            PlaceGiveMission(map, coid: 61001, missionId: 601);
            map.TriggerReactions(character, new List<long> { 61001 });

            Assert.AreEqual(1, character.CurrentQuests.Count, "repeatable mission may be granted again");
            Assert.AreEqual(601, character.CurrentQuests[0].MissionId);
            Assert.IsTrue(sent.OfType<GroupReactionCallPacket>().Any(p => p.Count > 0),
                "a genuine grant is sent to the client");
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
            SectorMap.SendGroupReactionCall = true;
        }
    }

    // ---- Completed-id create packet delivery ----

    [TestMethod]
    public void CreatePacket_WritesCompletedMissionIds_AsTrailingInt32Array()
    {
        var ids = new List<int> { 101, 202, 303 };

        var withIds = WriteExtended(ids);
        var without = WriteExtended(new List<int>());

        Assert.AreEqual(without.Length + ids.Count * sizeof(int), withIds.Length,
            "completed ids must add exactly 4 bytes each");

        // With every other tail count 0, completed ids are the trailing bytes.
        using var reader = new BinaryReader(new MemoryStream(withIds));
        reader.BaseStream.Position = withIds.Length - ids.Count * sizeof(int);
        Assert.AreEqual(101, reader.ReadInt32());
        Assert.AreEqual(202, reader.ReadInt32());
        Assert.AreEqual(303, reader.ReadInt32());
    }

    [TestMethod]
    public void CreatePacket_AbsoluteOffsets_MatchClientLayout()
    {
        var packet = new CreateCharacterExtendedPacket
        {
            ObjectId = new TFID(),
            CustomizedName = string.Empty,
            Name = string.Empty,
            ClanName = string.Empty,
            CompletedMissionIds = new List<int> { 554 },
            NumCompletedQuests = 1,
            NumCurrentQuests = 0,
            NumAchievements = 0,
            NumDisciplines = 0,
            NumSkills = 0,
        };
        packet.FirstTimeFlags[0] = 0xDEADBEEF;

        // Replicate SendGamePacket: 4-byte uint opcode prefix, then body.
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        writer.Flush();
        ms.SetLength(ms.Position);
        var bytes = ms.ToArray();

        using var reader = new BinaryReader(new MemoryStream(bytes));

        // Known-good anchor: FirstTimeFlags[0] at absolute 0x8EC.
        reader.BaseStream.Position = 0x8EC;
        Assert.AreEqual(0xDEADBEEFu, reader.ReadUInt32(), "FirstTimeFlags anchor at 0x8EC drifted");

        // NumCompletedQuests header at absolute 0x1A8.
        reader.BaseStream.Position = 0x1A8;
        Assert.AreEqual(1, reader.ReadInt32(), "NumCompletedQuests not at 0x1A8");

        // NumCurrentQuests header at absolute 0x1AC.
        reader.BaseStream.Position = 0x1AC;
        Assert.AreEqual(0, reader.ReadInt32(), "NumCurrentQuests not at 0x1AC");

        // With skills=0, the completed-id array begins at the tail start (absolute 0x1358).
        reader.BaseStream.Position = 0x1358;
        Assert.AreEqual(554, reader.ReadInt32(), "completed mission id not at tail start 0x1358");
    }

    private static byte[] WriteExtended(List<int> completedIds)
    {
        var packet = new CreateCharacterExtendedPacket
        {
            ObjectId = new TFID(),
            CustomizedName = string.Empty,
            Name = string.Empty,
            ClanName = string.Empty,
            CompletedMissionIds = completedIds,
            NumCompletedQuests = completedIds.Count,
            NumCurrentQuests = 0,
            NumAchievements = 0,
            NumDisciplines = 0,
            NumSkills = 0,
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        writer.Flush();
        return ms.ToArray();
    }

    // ---- diagnostic chat commands ----

    [TestMethod]
    public void ShowMissions_ReportsCompletedAndActive()
    {
        var o0 = MissionObjective.CreateForTests(7000, 0, 700, 3);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(700, o0));

        var character = new Character();
        character.SetCoid(7100, true);
        character.CompletedMissionIds.Add(554);
        var quest = new CharacterQuest(700, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1;
        character.CurrentQuests.Add(quest);

        var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/showMissions");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Completed (1): 554");
        StringAssert.Contains(result.Message, "mission 700 (seq 0, 1/3)");
    }

    [TestMethod]
    public void CharacterQuestWrite_MatchesClientActiveMissionRecordLayout()
    {
        var objective = MissionObjective.CreateForTests(8000, 0, 800, 4);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(800, objective));
        var quest = new CharacterQuest(800, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        quest.Write(writer);
        var bytes = stream.ToArray();

        Assert.AreEqual(CharacterQuest.StructureSize, bytes.Length);
        Assert.AreEqual(800, BitConverter.ToInt32(bytes, 0x00));
        Assert.AreEqual(0, BitConverter.ToInt32(bytes, 0x04));
        for (var offset = 0x08; offset <= 0x2C; offset += sizeof(int))
            Assert.AreEqual(-1, BitConverter.ToInt32(bytes, offset), $"saved mission state at 0x{offset:X}");
        Assert.AreEqual(8000, BitConverter.ToInt32(bytes, 0x30));
        Assert.AreEqual(0.5f, BitConverter.ToSingle(bytes, 0x34));
        Assert.AreEqual(0, BitConverter.ToInt32(bytes, 0x44));
    }

    [TestMethod]
    public void ClearAllMissions_ClearsMemoryAndInvokesDbDelete()
    {
        long? deleted = null;
        MissionPersistence.Instance.DeleteAllRows = coid => deleted = coid;

        var character = new Character();
        character.SetCoid(7200, true);
        character.CompletedMissionIds.Add(554);
        character.CurrentQuests.Add(new CharacterQuest(700, 0));

        var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/clearAllMissions");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, character.CompletedMissionIds.Count);
        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.AreEqual(7200L, deleted);
    }

    [TestMethod]
    public void RemoveCurrentMission_ClearsActiveOnlyAndInvokesActiveDbDelete()
    {
        long? activeDeleted = null;
        long? allDeleted = null;
        MissionPersistence.Instance.DeleteActiveRows = coid => activeDeleted = coid;
        MissionPersistence.Instance.DeleteAllRows = coid => allDeleted = coid;

        var character = new Character();
        character.SetCoid(7300, true);
        character.CompletedMissionIds.Add(554);
        character.CurrentQuests.Add(new CharacterQuest(700, 0));

        var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/removeCurrentMission");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CompletedMissionIds.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(554));
        Assert.AreEqual(7300L, activeDeleted);
        Assert.IsNull(allDeleted);
        StringAssert.Contains(result.Message, "Completed missions preserved");
    }

    [TestMethod]
    public void RemoveCurrentMission_NoCharacter_ReturnsMessage()
    {
        var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(null, "/removecurrentmission");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void GiveMission_AddsActiveQuestPersistsAndSendsClientPackets()
    {
        SeedMission(554, repeatable: 0, (5540, 0, 1));
        var manager = MissionPersistence.Instance;
        var writes = new List<(long Coid, int MissionId, QuestPersistKind Kind)>();
        manager.PersistQuestRow = (coid, missionId, op) => writes.Add((coid, missionId, op.Kind));

        var character = new Character();
        character.SetCoid(7400, true);
        var connection = new TNLConnection();
        character.SetOwningConnection(connection);

        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
        try
        {
            var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/giveMission 554");

            Assert.IsTrue(result.Handled);
            StringAssert.Contains(result.Message, "554");
            Assert.AreEqual(1, character.CurrentQuests.Count);
            Assert.AreEqual(554, character.CurrentQuests[0].MissionId);

            Assert.AreEqual(1, manager.FlushPending());
            CollectionAssert.AreEqual(
                new[] { (7400L, 554, QuestPersistKind.Upsert) },
                writes);

            Assert.IsTrue(sent.OfType<ConvoyMissionsResponsePacket>().Any(),
                "client journal must be pushed");
            Assert.IsTrue(sent.OfType<ObjectiveStatePacket>().Any(),
                "active objective state must be pushed");
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
        }
    }

    [TestMethod]
    public void GiveMission_UnknownMission_DoesNotGrant()
    {
        var character = new Character();
        character.SetCoid(7401, true);

        var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/giveMission 999999");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, character.CurrentQuests.Count);
        StringAssert.Contains(result.Message, "Unknown");
    }

    [TestMethod]
    public void GiveMission_UsageAndNoCharacter()
    {
        var usage = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(new Character(), "/giveMission");
        Assert.IsTrue(usage.Handled);
        StringAssert.Contains(usage.Message, "Usage");

        var invalid = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(new Character(), "/giveMission notanumber");
        Assert.IsTrue(invalid.Handled);
        StringAssert.Contains(invalid.Message, "Usage");

        var noChar = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(null, "/givemission 1");
        Assert.IsTrue(noChar.Handled);
        Assert.AreEqual("No character loaded.", noChar.Message);
    }

    [TestMethod]
    public void GiveMission_AlreadyActive_ResyncsWithoutDuplicate()
    {
        SeedMission(555, repeatable: 0, (5550, 0, 1));
        var character = new Character();
        character.SetCoid(7402, true);
        var quest = new CharacterQuest(555, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
        try
        {
            var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/givemission 555");

            Assert.IsTrue(result.Handled);
            Assert.AreEqual(1, character.CurrentQuests.Count);
            StringAssert.Contains(result.Message, "already active");
            Assert.IsTrue(sent.OfType<ConvoyMissionsResponsePacket>().Any());
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
        }
    }

    [TestMethod]
    public void CompleteMission_RemovesActivePersistsAndSendsClientPackets()
    {
        SeedMission(560, repeatable: 0, (5600, 0, 1));
        var manager = MissionPersistence.Instance;
        var writes = new List<(long Coid, int MissionId, QuestPersistKind Kind)>();
        manager.PersistQuestRow = (coid, missionId, op) => writes.Add((coid, missionId, op.Kind));

        var character = new Character();
        character.SetCoid(7500, true);
        var quest = new CharacterQuest(560, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => sent.Add(p);
        try
        {
            var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/completeMission 560");

            Assert.IsTrue(result.Handled);
            StringAssert.Contains(result.Message, "560");
            Assert.AreEqual(0, character.CurrentQuests.Count);
            Assert.IsTrue(character.CompletedMissionIds.Contains(560));

            Assert.AreEqual(1, manager.FlushPending());
            CollectionAssert.AreEqual(
                new[] { (7500L, 560, QuestPersistKind.Complete) },
                writes);

            Assert.IsTrue(sent.OfType<CompleteDynamicObjectivePacket>().Any(),
                "client complete-objective packet required");
            Assert.IsTrue(sent.OfType<ConvoyMissionsResponsePacket>().Any(),
                "client journal must be pushed");
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
        }
    }

    [TestMethod]
    public void CompleteMission_NotActive_DoesNotComplete()
    {
        SeedMission(561, repeatable: 0, (5610, 0, 1));
        var character = new Character();
        character.SetCoid(7501, true);

        var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/completeMission 561");

        Assert.IsTrue(result.Handled);
        Assert.IsFalse(character.CompletedMissionIds.Contains(561));
        StringAssert.Contains(result.Message, "not active");
    }

    [TestMethod]
    public void CompleteMission_AlreadyCompleted_ReportsStatus()
    {
        var character = new Character();
        character.SetCoid(7502, true);
        character.CompletedMissionIds.Add(562);

        var result = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(character, "/completeMission 562");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "already completed");
    }

    [TestMethod]
    public void CompleteMission_UsageAndNoCharacter()
    {
        var usage = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(new Character(), "/completeMission");
        Assert.IsTrue(usage.Handled);
        StringAssert.Contains(usage.Message, "Usage");

        var noChar = AutoCore.Game.Chat.ChatCommandService.Instance.Execute(null, "/completemission 1");
        Assert.IsTrue(noChar.Handled);
        Assert.AreEqual("No character loaded.", noChar.Message);
    }

    // ---- helpers ----

    private static void SeedMission(int missionId, byte repeatable, params (int ObjectiveId, byte Sequence, int CompleteCount)[] objectives)
    {
        var objs = objectives
            .Select(o => MissionObjective.CreateForTests(o.ObjectiveId, o.Sequence, missionId, o.CompleteCount))
            .ToArray();
        var mission = Mission.CreateForTests(missionId, objs);
        mission.IsRepeatable = repeatable;
        AssetManager.Instance.SetTestMission(mission);
    }

    private static (Character Character, SectorMap Map) CreatePlayer(int continentId)
    {
        var continent = new AutoCore.Database.World.Models.ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_mission_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(150, true);
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(151, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, map);
    }

    private static Reaction PlaceGiveMission(SectorMap map, int coid, int missionId)
    {
        var template = new ReactionTemplate
        {
            COID = coid,
            ReactionType = ReactionType.GiveMission,
            GenericVar1 = missionId,
        };
        var reaction = new Reaction(template);
        reaction.SetCoid(coid, false);
        reaction.SetMap(map);
        return reaction;
    }
}
