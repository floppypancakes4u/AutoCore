# Quick Start Guide

Get AutoCore running in 5 minutes!

## Prerequisites Checklist

- [ ] MySQL/MariaDB installed and running
- [ ] Auto Assault game files available
- [ ] .NET 8.0 runtime installed
- [ ] Project compiled successfully

## Quick Setup Steps

### 1. Create Databases (30 seconds)

```powershell
cd scripts
.\init-databases.ps1 -MySQLUser root -MySQLPassword YOUR_PASSWORD
```

If MySQL is not in PATH, specify the path:
```powershell
.\init-databases.ps1 -MySQLUser root -MySQLPath "C:\Program Files\MariaDB 12.1\bin\mysql.exe"
```

Or manually:
```sql
CREATE DATABASE autocore_auth;
CREATE DATABASE autocore_char;
CREATE DATABASE autocore_world;
```

### 2. Update Configuration Files (2 minutes)

Navigate to: `src\AutoCore.Launcher\bin\Debug\net8.0\`

Edit these files:

**appsettings.auth.json** - Update MySQL password:
```json
"AuthDatabaseConnectionString": "...Password=YOUR_PASSWORD;..."
```

**appsettings.global.json** - Update MySQL password and game path:
```json
"CharDatabaseConnectionString": "...Password=YOUR_PASSWORD;...",
"WorldDatabaseConnectionString": "...Password=YOUR_PASSWORD;...",
"GamePath": "C:\\Path\\To\\Auto Assault\\game\\Auto Assault new"
```

**appsettings.sector.json** - Update MySQL password and game path:
```json
"CharDatabaseConnectionString": "...Password=YOUR_PASSWORD;...",
"WorldDatabaseConnectionString": "...Password=YOUR_PASSWORD;...",
"GamePath": "C:\\Path\\To\\Auto Assault\\game\\Auto Assault new"
```

### 3. Run the Server (10 seconds)

```powershell
cd src\AutoCore.Launcher\bin\Debug\net8.0
.\AutoCore.Launcher.exe
```

## Verify It's Working

1. Check console output for "Server started" messages
2. Look for log files: `log-auth.txt`, `log-global.txt`, `log-sector.txt`
3. Verify no error messages in the console

## Common Issues

**"Unable to load assets!"**
- Check `GamePath` points to correct directory
- Verify `exe/autoassault.exe` exists in that path

**Database connection errors**
- Verify MySQL is running
- Check password in connection strings
- Ensure databases exist

**Port already in use**
- Check if another instance is running
- Change ports in appsettings files

## Default Account

A default admin account is automatically created when the database is first initialized:

- **Username:** `admin`
- **Password:** `admin123`

You can use these credentials to log in immediately after starting the server.

## Capturing Inventory Toss Packets (Dev)

When investigating dragging a cargo item onto the world (toss/delete), the sector server records related packets via the dev control API (enabled by default on port `27999`).

1. Start the server and connect a client.
2. Clear the capture log:

```powershell
Invoke-RestMethod -Method Delete http://127.0.0.1:27999/inventory-drop-log
```

3. In the client, drag a cargo item onto the world.
4. Read captured packets:

```powershell
Invoke-RestMethod http://127.0.0.1:27999/inventory-drop-log
```

World tosses from cargo use the `ItemDrop` opcode (`0x2057`), not `InventoryDrop`. Check sector logs for decoded fields (`unknown`, coid, tail bytes, and candidate integers).

For how to implement and test client-compatible packet layouts (padding, TFID, `SendGamePacket`), see [docs/networking.md](docs/networking.md).

On success the sector server sends, in order:

1. `ItemDropResponse` (`0x2058`) — echoes the **cargo item COID** from the request at offset `+0x8` (the client resolves this and destroys the dragged inventory object)
2. `InventoryCargoSendAll` — refreshes cargo after the drop

World toss currently **does not spawn a ground object**; the item is removed from server state only. This works for **cargo** items and for **equipped vehicle modules** after an `InventoryGrab` with `inventoryType=2` (the server tracks these as pending equipped drags between grab and drop/toss).

## Inventory test coverage gate

Run inventory-related unit tests with coverlet, then enforce the scoped **90%** line-coverage gate:

```powershell
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj `
  --filter "FullyQualifiedName~Inventory|FullyQualifiedName~ItemDrop|FullyQualifiedName~InventoryDropMM" `
  --collect:"XPlat Code Coverage" `
  --results-directory TestResults

powershell scripts/measure-scoped-coverage.ps1 -MinimumRate 90
```

The gate covers `src/AutoCore.Game/Inventory/*` (except `InventoryPersistence.cs`), `Entities/Vehicle.cs`, and `Packets/Sector/Inventory*` / `ItemDrop*` types. Debug-log helpers and `[ExcludeFromCodeCoverage]` world-loot grab internals are excluded from the scope.

## Next Steps

See `SETUP.md` for detailed configuration and troubleshooting.

