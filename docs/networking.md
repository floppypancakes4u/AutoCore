# Networking and packet layout

AutoCore talks to the retail Auto Assault client over TNL game RPC messages. Each message is a **`GameOpcode` (uint32)** followed by a **fixed-layout payload**. The client parses payloads by **byte offset**, not by field order in C# — so our packet classes must reproduce the exact sizes, padding, and reserved regions the client expects.

This document explains how to implement packet classes under `src/AutoCore.Game/Packets/` so they match login/character-selection patterns (where those work) and sector/inventory patterns (where layout is offset-driven).

## End-to-end send path

All server → client game packets should go through `TNLConnection.SendGamePacket`:

```67:88:src/AutoCore.Game/TNL/TNLConnection.cs
    public void SendGamePacket(BasePacket packet, RPCGuaranteeType type = RPCGuaranteeType.RPCGuaranteedOrdered, bool skipOpcode = false)
    {
        // ...
        using (var stream = new MemoryStream(0x4000))
        using (var writer = new BinaryWriter(stream))
        {
            if (!skipOpcode)
                writer.Write(packet.Opcode);

            packet.Write(writer);

            stream.SetLength(stream.Position);

            arr = stream.ToArray();
        }
```

Important behaviors:

| Step | Responsibility |
|------|----------------|
| `writer.Write(packet.Opcode)` | **Outside** the packet class (unless `skipOpcode: true` for special cases like `MapInfo`) |
| `packet.Write(writer)` | Payload only, starting at offset **+0x04** relative to the final on-wire buffer |
| `stream.SetLength(stream.Position)` | **Required** whenever `Write` advances `Position` over holes (padding). Without this, `MemoryStream.ToArray()` truncates and the client reads garbage |

`packet.Write` must **never** write the opcode. Login and sector packets follow the same rule.

## Packet class contract

Every game packet inherits `BasePacket`:

```6:12:src/AutoCore.Game/Packets/BasePacket.cs
public abstract class BasePacket : IOpcodedPacket<GameOpcode>
{
    public abstract GameOpcode Opcode { get; }

    public virtual void Read(BinaryReader reader) => throw new NotSupportedException();
    public virtual void Write(BinaryWriter writer) => throw new NotSupportedException();
}
```

Convention:

- **`Opcode`** — single `GameOpcode` value; must match `GameOpcode.cs` and client handlers.
- **`Read`** — client → server only (handler receives `BinaryReader` **after** the opcode uint32 was consumed).
- **`Write`** — server → client only; starts at payload offset +0x00 (which is wire offset +0x04 after `SendGamePacket` writes the opcode).

Place packets by connection phase:

| Folder | Examples |
|--------|----------|
| `Packets/Login/` | `LoginRequestPacket`, `LoginResponsePacket`, `LoginNewCharacterPacket` |
| `Packets/Global/` | `LoginAckPacket`, `TransferToSectorPacket` |
| `Packets/Sector/` | Inventory, movement, create-object, `ItemDrop*` |

## Layout style A — sequential (login / character selection)

Login-phase packets are mostly **dense sequential fields**. Padding appears only where the client layout has explicit gaps.

**Outgoing example** — `LoginNewCharacterResponsePacket`:

```12:16:src/AutoCore.Game/Packets/Login/LoginNewCharacterResponsePacket.cs
    public override void Write(BinaryWriter writer)
    {
        writer.Write(Result);
        writer.Write(NewCharCoid);
    }
```

**Incoming example** — `LoginRequestPacket` reads strings then skips 2 bytes before uint32 fields:

```14:23:src/AutoCore.Game/Packets/Login/LoginRequestPacket.cs
    public override void Read(BinaryReader reader)
    {
        Username = reader.ReadUTF8StringOn(33);
        Password = reader.ReadUTF8StringOn(33);

        reader.BaseStream.Position += 2;

        UserId = reader.ReadUInt32();
        AuthKey = reader.ReadUInt32();
    }
```

**Incoming with mid-packet padding** — `LoginNewCharacterPacket`:

```35:63:src/AutoCore.Game/Packets/Login/LoginNewCharacterPacket.cs
    public override void Read(BinaryReader reader)
    {
        CBID = reader.ReadInt32();
        PlayerName = reader.ReadUTF8StringOn(33);
        // ... more fields ...
        VehicleTrim = reader.ReadByte();

        reader.BaseStream.Position += 3;

        ScaleOffset = reader.ReadSingle();
        WheelsetCBID = reader.ReadInt32();
        VehicleName = reader.ReadUTF8StringOn(33);
    }
```

Use this style when Ghidra/client decompilation shows fields packed back-to-back with only small `+N` skips.

## Layout style B — fixed offsets (sector / inventory)

Sector packets (especially inventory) use **absolute offsets** from the start of the payload (+0x04 from wire start). Implement them by:

1. Declaring a `MinimumLength` or documented size constant when the client expects a fixed total size.
2. Using `writer.BaseStream.Position = offset` or `+= padding` to skip reserved bytes.
3. Ending `Write` by setting `Position` to the final packet size so trailing reserved space is zero-filled via `SetLength`.

### TFID fields (16 bytes)

Many sector packets embed a **TFID** (object identity): `int64 coid`, `bool global`, then **7 reserved bytes**. Always use the shared helpers:

```10:20:src/AutoCore.Game/Extensions/BinaryWriterExtensions.cs
    public static void WriteTFID(this BinaryWriter writer, long coid, bool global)
    {
        writer.Write(coid);
        writer.Write(global);
        writer.Write(TFIDPadding);
    }
```

```8:18:src/AutoCore.Game/Extensions/BinaryReaderExtensions.cs
    public static TFID ReadTFID(this BinaryReader reader)
    {
        var id = new TFID
        {
            Coid = reader.ReadInt64(),
            Global = reader.ReadBoolean()
        };

        reader.BaseStream.Position += 7;

        return id;
    }
```

Do **not** hand-roll TFID size; off-by-one in the 7-byte tail corrupts every following field.

### Outgoing inventory examples

**`InventoryEquipPacket`** — padding at +0x04, three TFIDs, trailing reserved bytes:

```29:43:src/AutoCore.Game/Packets/Sector/InventoryEquipPacket.cs
    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;
        writer.WriteTFID(ItemId);
        writer.WriteTFID(VehicleId);
        writer.WriteTFID(OldItemId);
        writer.Write(PutInHand);
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.Write(InventoryTypeFrom);
        writer.BaseStream.Position += 4;
    }
```

**`InventoryGrabResponsePacket`** — multiple skip regions; total payload **0x38** bytes (0x3C including opcode):

```19:33:src/AutoCore.Game/Packets/Sector/InventoryGrabResponsePacket.cs
    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;
        writer.WriteTFID(ItemCoid, ItemGlobal);
        writer.Write(InventoryType);
        writer.BaseStream.Position += 3;
        writer.Write(Quantity);
        writer.Write(AddToExistingItem);
        writer.BaseStream.Position += 7;
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.BaseStream.Position += 8;
        writer.Write(WasSuccessful);
        writer.BaseStream.Position += 3;
    }
```

**`ItemDropResponsePacket`** — jump to reserved tail and success byte:

```27:40:src/AutoCore.Game/Packets/Sector/ItemDropResponsePacket.cs
    public override void Write(BinaryWriter writer)
    {
        writer.Write(SourceObjectId);
        writer.Write(ItemCoid);
        writer.Write(DropPosition.X);
        writer.Write(DropPosition.Y);
        writer.Write(DropPosition.Z);
        writer.BaseStream.Position = 0x1C;
        writer.BaseStream.Position += 12;
        writer.Write(TailValue);
        writer.BaseStream.Position = 0x30;
        writer.Write(WasSuccessful);
        writer.BaseStream.Position = MinimumLength;
    }
```

**`CreateSimpleObjectPacket`** — large create payload with interleaved skips and fixed-width strings (`WriteUtf8StringOn`). Use as the reference for spawning items into the client inventory/world.

### Incoming sector packets — capture `RawBytes`

When client layout is still being reverse-engineered, or fields vary by `inventoryType`, **read the full buffer from the opcode** and parse by offset:

```16:38:src/AutoCore.Game/Packets/Sector/ItemDropPacket.cs
    public override void Read(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var start = stream.Position - sizeof(uint);
        // ...
        stream.Position = start;
        RawBytes = reader.ReadBytes((int)remaining);

        if (RawBytes.Length >= 8)
            SourceObjectId = BitConverter.ToInt32(RawBytes, 4);
        // ...
    }
```

The same pattern is used for `InventoryDropPacket`, `InventoryGrabPacket`, and `InventoryDropMMPacket`. Handlers should log `RawBytes` (hex) until field layout is confirmed in Ghidra.

## Implementing a new packet (checklist)

1. **Add `GameOpcode`** in `src/AutoCore.Game/Constants/GameOpcode.cs` (match client opcode value).
2. **Create `*Packet.cs`** in the correct `Packets/` subfolder inheriting `BasePacket`.
3. **Document offsets** in XML comments (see `InventoryEquipPacket`); cite Ghidra symbol or live capture when possible.
4. **Implement `Write` or `Read`** using the appropriate layout style (sequential vs offset/jump).
5. **Wire the handler** in `TNLConnection` (`HandlePacket` switch) or the relevant `TNLConnection.*.cs` partial.
6. **Send via `SendGamePacket`** from gameplay code — do not build raw byte arrays in managers.
7. **Add unit tests** under `src/AutoCore.Game.Tests/Packets/` that assemble the packet **the same way as production**:

```csharp
using var stream = new MemoryStream();
using var writer = new BinaryWriter(stream);
writer.Write((uint)packet.Opcode);   // mirrors SendGamePacket
packet.Write(writer);
stream.SetLength(stream.Position);     // mirrors SendGamePacket
var bytes = stream.ToArray();
// assert bytes.Length and per-offset values
```

Existing references:

- `InventoryGrabResponsePacketTests` — byte-exact 0x3C layout
- `ItemDropPacketTests` — live capture hex fixture
- `ItemDropResponsePacketTests` — success byte at +0x30
- `InventoryDropPacketTests` / `InventoryDropMMPacketTests` — read + tail/candidate helpers

## Inventory flow conventions

These rules are enforced by client code paths (Ghidra) and covered by regression tests:

| Opcode | Direction | Notes |
|--------|-----------|-------|
| `ItemDrop` (0x2057) | Client → server | World toss; **not** `InventoryDrop` |
| `ItemDropResponse` (0x2058) | Server → client | Echo **dragged item COID** at +0x8, not a spawned world COID |
| `InventoryDrop` (0x2036) | Client → server | Cargo/locker slot move (`inventoryType` 1 or 3) / hardpoint equip (`2`) |
| `InventoryGrabMM` (0x2038) | Client → server | Mass Move grab (same fields as Grab); server reuses Grab, replies with **GrabResponse 0x2035** (client early-outs on 0x2039) |
| `InventoryDropMM` (0x203A) | Client → server | Mass Move drop (same fields as Drop); server reuses Drop, replies with **DropResponse 0x2037** (client early-outs on 0x203B) |
| `InventoryEquip` (0x203C) | Server → client | Hardpoint equip ack; **no** `InventoryDropResponse` with `inventoryType=2` |
| `InventoryUnequip` (0x203E) | Server → client | Sent **before** `InventoryGrabResponse` on equipped grab |
| `InventoryCargoSendAll` | Server → client | Refresh cargo after mutations |

World toss currently deletes inventory server-side only (no ground spawn). See [QUICKSTART.md](../QUICKSTART.md) for operator notes.

## Fragmentation

`SendGamePacket` fragments payloads larger than **1400 bytes** into ~220-byte RPC chunks. Fixed-size packets (for example `InventoryCargoSendAll`) must still write every slot entry; do not truncate the loop because of size — fragmentation handles it.

## Common mistakes

| Mistake | Symptom |
|---------|---------|
| Writing opcode inside `packet.Write` | Client reads shifted fields; every offset wrong |
| Skipping `stream.SetLength` after `Position +=` | Short buffers, uninitialized tail bytes, client crash |
| Custom TFID size | All following fields misaligned |
| Using `InventoryDrop` for world toss | Wrong handler; client error strings / no-op |
| `ItemDropResponse` with world loot COID | Client destroys wrong object / crash |
| Guessing offsets without hex test | Silent corruption; add a capture test first |

## Related source

| Area | Path |
|------|------|
| Send path | `src/AutoCore.Game/TNL/TNLConnection.cs` |
| Sector handlers | `src/AutoCore.Game/TNL/TNLConnection.Sector.cs` |
| Login handlers | `src/AutoCore.Game/TNL/TNLConnection.Login.cs` |
| Opcodes | `src/AutoCore.Game/Constants/GameOpcode.cs` |
| Binary helpers | `src/AutoCore.Game/Extensions/BinaryWriterExtensions.cs`, `BinaryReaderExtensions.cs` |
| Inventory builders | `src/AutoCore.Game/Inventory/InventoryPacketFactory.cs` |
