namespace AutoCore.Game.TNL;

/// <summary>
/// In-world same-connection map-transfer handshake. Initial Sector login stays
/// <see cref="None"/> and uses the existing Stage1/2/3 handlers.
/// </summary>
public enum SectorTransferPhase : byte
{
    None = 0,
    WaitingForStage2 = 1,
    WaitingForStage3Ack = 2,
}
