namespace AutoCore.Game.Inventory;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Entities;

public sealed class InventoryRuntime : IInventoryRuntime
{
    private readonly Character _character;

    public InventoryRuntime(Character character)
    {
        _character = character;
    }

    public bool CanAllocateItem => _character?.Map != null;

    public InventoryManager Inventory => _character?.Inventory;

    public long CharacterCoid => _character?.ObjectId.Coid ?? 0;

    /// <summary>Context factory for the allocator (tests inject InMemory).</summary>
    internal static Func<CharContext> CreateContext { get; set; } = static () => new CharContext();

    /// <summary>
    /// Persistent-coid source; production reserves a row in the simple_object sequence.
    /// Test seam so unit tests can use a plain counter.
    /// </summary>
    internal static Func<long> AllocatePersistentCoid { get; set; } = AllocateFromSimpleObjectSequence;

    /// <summary>
    /// SS-31: persistent item coids must come from the same authority that mints character /
    /// vehicle / equipment coids — the simple_object sequence. The previous source
    /// (<c>Map.LocalCoidCounter</c>, checked only against the character's own inventory)
    /// leapfrogged the DB sequence: items 18274/18275 collided with character 18274 /
    /// vehicle 18275, EnsureSimpleObject overwrote their identity rows, and the client
    /// access-violated at character select. Reserving the coid by inserting a placeholder
    /// row makes a collision structurally impossible; the item persist fills the row in.
    /// </summary>
    public long AllocateItemCoid() => AllocatePersistentCoid();

    /// <summary>Reserve the next simple_object coid with a Type=0 placeholder row.</summary>
    internal static long AllocateFromSimpleObjectSequence()
    {
        using var context = CreateContext();
        var row = new SimpleObjectData { Type = 0, CBID = 0 };
        context.SimpleObjects.Add(row);
        context.SaveChanges();
        return row.Coid;
    }
}
