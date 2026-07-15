/*
 * Purpose: Send a sized sector game packet buffer through the net connection.
 * Stable ID: aa_exe_00807460
 * Address: 0x00807460
 * Used by RespawnInSector and other Client_Send* helpers.
 */

// Signature inferred: Client_SendSectorPacket(game, size, payload)
// Forwards to connection send with guarantee flags.
