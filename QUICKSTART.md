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

## Next Steps

See `SETUP.md` for detailed configuration and troubleshooting.

