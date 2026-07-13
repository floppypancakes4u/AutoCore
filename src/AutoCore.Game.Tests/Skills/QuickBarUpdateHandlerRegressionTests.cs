using System.Reflection;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Skills;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Skills;

/// <summary>
/// End-to-end regression for <c>HandleQuickBarUpdatePacket</c> (C2S 0x2062).
/// Client is optimistic; server must persist so CreateCharacterExtended can restore on login.
/// </summary>
[TestClass]
public class QuickBarUpdateHandlerRegressionTests
{
    private int _persistCalls;

    [TestInitialize]
    public void SetUp()
    {
        _persistCalls = 0;
        CharacterSkillService.PersistForTests = _ => Interlocked.Increment(ref _persistCalls);
        AssetManager.Instance.ClearTestSkills();
    }

    [TestCleanup]
    public void TearDown()
    {
        CharacterSkillService.PersistForTests = null;
        AssetManager.Instance.ClearTestSkills();
    }

    [TestMethod]
    public void ApplySkillSlot_FromLiveCapture_PersistsSkillAndClearsItem()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9101);
        character.LearnedSkills[2103] = 1;
        character.QuickBarItemCoids[0] = 99999; // must be cleared by skill place
        connection.CurrentCharacter = character;

        // Live capture: slot 0, skill, value 0x837 = 2103
        InvokeHandler(connection, Convert.FromHexString("0000D6343708000000000000"));

        Assert.AreEqual(2103, character.QuickBarSkills[0]);
        Assert.AreEqual(-1L, character.QuickBarItemCoids[0]);
        Assert.IsTrue(_persistCalls >= 1);
    }

    [TestMethod]
    public void ApplyItemSlot_PersistsItemAndClearsSkill()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9102);
        character.LearnedSkills[2103] = 1;
        character.QuickBarSkills[4] = 2103;
        character.Inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(100, CloneBaseObjectType.Item, "kit", 0xABC, 0, 0, 1),
        });
        connection.CurrentCharacter = character;

        InvokeHandler(connection, BuildBody(slot: 4, isItem: 1, value: 0xABC));

        Assert.AreEqual(0xABCL, character.QuickBarItemCoids[4]);
        Assert.AreEqual(0, character.QuickBarSkills[4]);
        Assert.IsTrue(_persistCalls >= 1);
    }

    [TestMethod]
    public void ClearSlot_EmptiesSkillAndItem()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9103);
        character.LearnedSkills[2103] = 1;
        character.QuickBarSkills[2] = 2103;
        character.QuickBarItemCoids[2] = 55;
        connection.CurrentCharacter = character;

        // Client clear path: IsItem=1, Value=-1
        InvokeHandler(connection, BuildBody(slot: 2, isItem: 1, value: -1L));

        Assert.AreEqual(0, character.QuickBarSkills[2]);
        Assert.AreEqual(-1L, character.QuickBarItemCoids[2]);
    }

    [TestMethod]
    public void Reject_UnlearnedSkill_DoesNotMutateOrPersist()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9104);
        connection.CurrentCharacter = character;
        _persistCalls = 0;

        InvokeHandler(connection, BuildBody(slot: 0, isItem: 0, value: 8888));

        Assert.AreEqual(0, character.QuickBarSkills[0]);
        Assert.AreEqual(0, _persistCalls);
    }

    [TestMethod]
    public void Reject_ItemNotInCargo_DoesNotMutate()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9105);
        connection.CurrentCharacter = character;
        _persistCalls = 0;

        InvokeHandler(connection, BuildBody(slot: 1, isItem: 1, value: 12345));

        Assert.AreEqual(-1L, character.QuickBarItemCoids[1]);
        Assert.AreEqual(0, _persistCalls);
    }

    [TestMethod]
    public void Reject_InvalidSlot_OutOfRange()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9106);
        character.LearnedSkills[2103] = 1;
        connection.CurrentCharacter = character;
        _persistCalls = 0;

        // slot is a single byte so 100 is max we can send; service rejects >= 100
        InvokeHandler(connection, BuildBody(slot: 100, isItem: 0, value: 2103));

        Assert.AreEqual(0, character.QuickBarSkills[0]);
        Assert.AreEqual(0, _persistCalls);
    }

    [TestMethod]
    public void ShortBody_IsIgnored_NoPersist()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9107);
        connection.CurrentCharacter = character;
        _persistCalls = 0;

        InvokeHandler(connection, new byte[] { 0x00, 0x00 });

        Assert.AreEqual(0, _persistCalls);
        Assert.AreEqual(0, character.QuickBarSkills[0]);
    }

    [TestMethod]
    public void EmptyBody_IsIgnored()
    {
        var connection = new TNLConnection();
        connection.CurrentCharacter = MakeCharacter(9108);
        _persistCalls = 0;

        InvokeHandler(connection, Array.Empty<byte>());
        Assert.AreEqual(0, _persistCalls);
    }

    [TestMethod]
    public void NullCurrentCharacter_DoesNotThrow()
    {
        var connection = new TNLConnection();
        connection.CurrentCharacter = null;
        _persistCalls = 0;

        InvokeHandler(connection, BuildBody(slot: 0, isItem: 0, value: 2103));
        Assert.AreEqual(0, _persistCalls);
    }

    [TestMethod]
    public void SkillNegativeValue_ClearsSlot()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9109);
        character.LearnedSkills[2103] = 1;
        character.QuickBarSkills[6] = 2103;
        connection.CurrentCharacter = character;

        InvokeHandler(connection, BuildBody(slot: 6, isItem: 0, value: -1L));

        Assert.AreEqual(0, character.QuickBarSkills[6]);
        Assert.AreEqual(-1L, character.QuickBarItemCoids[6]);
        Assert.IsTrue(_persistCalls >= 1);
    }

    [TestMethod]
    public void ItemCoidZero_NormalizesToEmpty()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9110);
        character.QuickBarItemCoids[8] = 77;
        connection.CurrentCharacter = character;

        InvokeHandler(connection, BuildBody(slot: 8, isItem: 1, value: 0L));

        Assert.AreEqual(-1L, character.QuickBarItemCoids[8]);
        Assert.AreEqual(0, character.QuickBarSkills[8]);
    }

    [TestMethod]
    public void HighSlot_NinetyNine_Accepted()
    {
        var connection = new TNLConnection();
        var character = MakeCharacter(9111);
        character.LearnedSkills[42] = 2;
        connection.CurrentCharacter = character;

        InvokeHandler(connection, BuildBody(slot: 99, isItem: 0, value: 42));

        Assert.AreEqual(42, character.QuickBarSkills[99]);
        Assert.AreEqual(-1L, character.QuickBarItemCoids[99]);
    }

    private static Character MakeCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        var dbData = new CharacterData { Coid = coid, Name = "QB", Level = 5, SkillPoints = 0 };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }

    private static byte[] BuildBody(byte slot, byte isItem, long value, ushort pad = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(slot);
        writer.Write(isItem);
        writer.Write(pad);
        writer.Write(value);
        writer.Flush();
        return stream.ToArray();
    }

    private static void InvokeHandler(TNLConnection connection, byte[] body)
    {
        var method = typeof(TNLConnection).GetMethod(
            "HandleQuickBarUpdatePacket",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "HandleQuickBarUpdatePacket must exist on TNLConnection");
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }
}
