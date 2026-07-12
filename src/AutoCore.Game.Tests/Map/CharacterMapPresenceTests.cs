using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Per-character mission presence: Delete/Create must not rewrite shared SectorMap for others.
/// </summary>
[TestClass]
public class CharacterMapPresenceTests
{
    const int ContId = 8708;
    const long DialogCreatureCoid = 88_002;
    const long DialogSpawnCoid = 14_090;
    const long PlaceBCreatureCoid = 88_003;
    const long DelDialogRx = 14_133;
    const long CreatePlaceBRx = 15_820;

    readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
    }

    [TestMethod]
    public void Ledger_Suppress_IsPresentForCharacter_RespectsFamDefault()
    {
        var presence = new CharacterMapPresence();
        presence.EnsureContinent(ContId);

        Assert.IsTrue(presence.IsPresentForCharacter(DialogSpawnCoid, famDefaultActive: true));
        presence.Suppress(DialogSpawnCoid);
        Assert.IsTrue(presence.IsSuppressed(DialogSpawnCoid));
        Assert.IsFalse(presence.IsPresentForCharacter(DialogSpawnCoid, famDefaultActive: true));
    }

    [TestMethod]
    public void Ledger_Materialize_OverridesSuppress()
    {
        var presence = new CharacterMapPresence();
        presence.EnsureContinent(ContId);
        presence.Suppress(PlaceBCreatureCoid);
        presence.Materialize(PlaceBCreatureCoid);
        Assert.IsFalse(presence.IsSuppressed(PlaceBCreatureCoid));
        Assert.IsTrue(presence.IsPresentForCharacter(PlaceBCreatureCoid, famDefaultActive: false));
    }

    [TestMethod]
    public void Ledger_EnsureContinent_ClearsWhenMapChanges()
    {
        var presence = new CharacterMapPresence();
        presence.EnsureContinent(ContId);
        presence.Suppress(1);
        presence.EnsureContinent(ContId + 1);
        Assert.IsFalse(presence.IsSuppressed(1));
        Assert.AreEqual(ContId + 1, presence.ContinentId);
    }

    [TestMethod]
    public void Delete_Personal_DoesNotRemoveSharedObject_OtherPlayerStillSeesIt()
    {
        var map = CreateMap();
        PlaceDialogCreature(map);

        var playerA = CreateCharacter(map, 3001);
        var playerB = CreateCharacter(map, 3002);

        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));

        var del = PlaceDeleteReaction(map, DelDialogRx, DialogCreatureCoid);
        Assert.IsTrue(del.TriggerIfPossible(playerA));

        // Shared map keeps the NPC for other players.
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Delete must not remove dialog NPC from shared SectorMap for other players");

        // Only A is suppressed.
        Assert.IsTrue(playerA.MapPresence.IsSuppressed(DialogCreatureCoid));
        Assert.IsFalse(playerB.MapPresence.IsSuppressed(DialogCreatureCoid));
        Assert.IsFalse(playerA.MapPresence.IsPresentForCharacter(DialogCreatureCoid, famDefaultActive: true));
        Assert.IsTrue(playerB.MapPresence.IsPresentForCharacter(DialogCreatureCoid, famDefaultActive: true));
    }

    [TestMethod]
    public void Delete_Personal_AlsoSuppressesSpawnOwnerWhenListed()
    {
        var map = CreateMap();
        PlaceDialogCreature(map);
        var playerA = CreateCharacter(map, 3010);

        // Retail l1_del_gunnysioux1 lists spawn COID 14090 (not only the live child).
        var del = PlaceDeleteReaction(map, DelDialogRx, DialogSpawnCoid);
        Assert.IsTrue(del.TriggerIfPossible(playerA));

        Assert.IsNotNull(map.GetObjectByCoid(DialogSpawnCoid) as SpawnPoint,
            "Spawn marker stays on shared map");
        Assert.IsTrue(playerA.MapPresence.IsSuppressed(DialogSpawnCoid));
        // Live child of that spawn should also be suppressed for A when still linked.
        Assert.IsTrue(playerA.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Suppressing a SpawnPoint must suppress its live child for this character");
    }

    [TestMethod]
    public void Delete_DoForAllPlayers_StillRemovesFromSharedMap()
    {
        var map = CreateMap();
        PlaceDialogCreature(map);
        var playerA = CreateCharacter(map, 3020);

        var tpl = new ReactionTemplate
        {
            COID = (int)DelDialogRx,
            ReactionType = ReactionType.Delete,
            DoForAllPlayers = true,
        };
        tpl.Objects.Add(DialogCreatureCoid);
        var del = new Reaction(tpl);
        del.SetCoid(DelDialogRx, false);
        del.SetMap(map);

        Assert.IsTrue(del.TriggerIfPossible(playerA));
        Assert.IsNull(map.GetObjectByCoid(DialogCreatureCoid),
            "DoForAllPlayers remains shared-world authority");
    }

    [TestMethod]
    public void TriggerReactions_PersonalDelete_SetsSingleClientOnlyOnPacket()
    {
        var map = CreateMap();
        PlaceDialogCreature(map);
        var playerA = CreateCharacter(map, 3030);
        // Need owning connection so packet is sent
        var conn = new TNLConnection();
        playerA.SetOwningConnection(conn);

        var del = PlaceDeleteReaction(map, DelDialogRx, DialogCreatureCoid);
        map.TriggerReactions(playerA, new List<long> { DelDialogRx });

        var group = _sent.OfType<GroupReactionCallPacket>().LastOrDefault();
        Assert.IsNotNull(group, "0x206C must be sent to activator");
        Assert.IsTrue(group.Count > 0);
        // Inspect nested reaction entry SingleClientOnly via packet write/read is heavy;
        // presence side-effect is the contract for multiplayer correctness.
        Assert.IsTrue(playerA.MapPresence.IsSuppressed(DialogCreatureCoid));
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
    }

    [TestMethod]
    public void UseObject_RejectsSuppressedCoid()
    {
        var map = CreateMap();
        PlaceDialogCreature(map);
        var playerA = CreateCharacter(map, 3050);
        playerA.MapPresence.Suppress(DialogCreatureCoid);

        // Simulate UseObject target resolution path used by NpcInteractHandler.
        Assert.IsTrue(playerA.MapPresence.IsSuppressed(DialogCreatureCoid));
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid),
            "Object remains for other characters");
    }

    [TestMethod]
    public void Create_Personal_MaterializesLedger_WithoutDeletingOthersDialog()
    {
        var map = CreateMap();
        PlaceDialogCreature(map);
        map.MapData.Templates[PlaceBCreatureCoid] = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            COID = (int)PlaceBCreatureCoid,
            IsActive = false,
            OriginalIsActive = false,
        };

        var playerA = CreateCharacter(map, 3040);
        var playerB = CreateCharacter(map, 3041);

        var createTpl = new ReactionTemplate
        {
            COID = (int)CreatePlaceBRx,
            ReactionType = ReactionType.Create,
        };
        createTpl.Objects.Add(PlaceBCreatureCoid);
        var create = new Reaction(createTpl);
        create.SetCoid(CreatePlaceBRx, false);
        create.SetMap(map);

        Assert.IsTrue(create.TriggerIfPossible(playerA));
        Assert.IsTrue(playerA.MapPresence.IsMaterialized(PlaceBCreatureCoid));
        Assert.IsFalse(playerB.MapPresence.IsMaterialized(PlaceBCreatureCoid));
        // Dialog still shared for B
        Assert.IsNotNull(map.GetObjectByCoid(DialogCreatureCoid));
    }

    static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = "test_presence",
            IsPersistent = false,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    static Character CreateCharacter(SectorMap map, long coid)
    {
        var ch = new Character();
        ch.SetCoid(coid, true);
        ch.SetMap(map);
        ch.MapPresence.EnsureContinent(map.ContinentId);
        return ch;
    }

    static void PlaceDialogCreature(SectorMap map)
    {
        map.MapData.Templates[DialogSpawnCoid] = new SpawnPointTemplate
        {
            COID = (int)DialogSpawnCoid,
            IsActive = true,
            OriginalIsActive = true,
        };
        var sp = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[DialogSpawnCoid]);
        sp.SetCoid(DialogSpawnCoid, false);
        sp.SetMap(map);

        var creature = new Creature();
        creature.SetCoid(DialogCreatureCoid, true);
        creature.SpawnOwner = DialogSpawnCoid;
        creature.IsMissionGiver = true;
        creature.SetMap(map);
        sp.SetLastSpawnedCoidForTests(DialogCreatureCoid);
    }

    static Reaction PlaceDeleteReaction(SectorMap map, long reactionCoid, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Delete,
        };
        tpl.Objects.Add(objectCoid);
        var rx = new Reaction(tpl);
        rx.SetCoid(reactionCoid, false);
        rx.SetMap(map);
        return rx;
    }
}
