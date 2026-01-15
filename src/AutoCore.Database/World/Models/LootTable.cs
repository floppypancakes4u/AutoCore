namespace AutoCore.Database.World.Models;

/// <summary>
/// Represents a loot table definition from wad.xml tLootTable section.
/// Loot tables define procedural generation parameters for item drops.
/// </summary>
public class LootTable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Drop parameters
    public short LootRolls { get; set; }
    public float DropChance { get; set; }
    public float ConsumableDropChance { get; set; }

    // Level adjustment
    public float DropLevelOffset { get; set; }
    public short MaxLevelOffset { get; set; }
    public float LevelOffsetMultiplier { get; set; }

    // Enhancement parameters
    public short MaxEnhancementComplexity { get; set; }
    public int BaseChanceEnhanced { get; set; }
    public int ChanceEnhancedModifierPerLevel { get; set; }

    // Item type chances (weighted)
    public int ChanceWeapon { get; set; }
    public int ChanceArmor { get; set; }
    public int ChancePowerPlant { get; set; }
    public int ChanceWheelSet { get; set; }
    public int ChanceVehicle { get; set; }
    public int ChanceGadget { get; set; }
    public int ChanceTinkeringKit { get; set; }
    public int ChanceAccessory { get; set; }
    public int ChanceRaceItem { get; set; }
    public int ChanceOrnament { get; set; }
    public int ChanceOther { get; set; }

    // Rarity chances (weighted) - indexes 0-8
    public int ChanceRarity0 { get; set; }
    public int ChanceRarity1 { get; set; }
    public int ChanceRarity2 { get; set; }
    public int ChanceRarity3 { get; set; }
    public int ChanceRarity4 { get; set; }
    public int ChanceRarity5 { get; set; }
    public int ChanceRarity6 { get; set; }
    public int ChanceRarity7 { get; set; }
    public int ChanceRarity8 { get; set; }

    // Credits drop
    public float DropCreditsChance { get; set; }
    public int MinCreditsDrop { get; set; }
    public int MaxCreditsDrop { get; set; }

    // Broken item modifiers
    public float WeaponBrokenModifier { get; set; }
    public float ArmorBrokenModifier { get; set; }
    public float PowerPlantBrokenModifier { get; set; }
    public float WheelSetBrokenModifier { get; set; }
    public float VehicleBrokenModifier { get; set; }
    public float GadgetBrokenModifier { get; set; }
    public float TinkeringKitBrokenModifier { get; set; }
    public float AccessoryBrokenModifier { get; set; }
    public float RaceItemBrokenModifier { get; set; }
    public float OrnamentBrokenModifier { get; set; }
    public float OtherBrokenModifier { get; set; }

    /// <summary>
    /// Gets the total weight of all item type chances.
    /// </summary>
    public int GetTotalItemTypeWeight()
    {
        return ChanceWeapon + ChanceArmor + ChancePowerPlant + ChanceWheelSet +
               ChanceVehicle + ChanceGadget + ChanceTinkeringKit + ChanceAccessory +
               ChanceRaceItem + ChanceOrnament + ChanceOther;
    }

    /// <summary>
    /// Gets the total weight of all rarity chances.
    /// </summary>
    public int GetTotalRarityWeight()
    {
        return ChanceRarity0 + ChanceRarity1 + ChanceRarity2 + ChanceRarity3 +
               ChanceRarity4 + ChanceRarity5 + ChanceRarity6 + ChanceRarity7 + ChanceRarity8;
    }

    /// <summary>
    /// Gets the rarity chance by index (0-8).
    /// </summary>
    public int GetRarityChance(int index)
    {
        return index switch
        {
            0 => ChanceRarity0,
            1 => ChanceRarity1,
            2 => ChanceRarity2,
            3 => ChanceRarity3,
            4 => ChanceRarity4,
            5 => ChanceRarity5,
            6 => ChanceRarity6,
            7 => ChanceRarity7,
            8 => ChanceRarity8,
            _ => 0
        };
    }
}
