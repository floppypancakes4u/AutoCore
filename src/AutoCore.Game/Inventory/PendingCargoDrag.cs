namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;

/// <summary>
/// Cargo stack peeled onto the cursor (not occupying a grid slot until drop).
/// </summary>
public readonly record struct PendingCargoDrag(
    int Cbid,
    CloneBaseObjectType Type,
    string DisplayName,
    long Coid,
    int Quantity,
    long SourceCoid,
    bool Global);
