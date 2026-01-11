# AutoCore Server Setup Guide

This guide will help you set up and run the AutoCore game server.

## Prerequisites

1. **MySQL/MariaDB Database Server**
   - Install MySQL 8.0+ or MariaDB 10.5+
   - Ensure the MySQL service is running
   - Note your MySQL root password (or create a dedicated user)

2. **Auto Assault Game Files**
   - You need a copy of the Auto Assault game installation
   - The game directory must contain:
     - `exe/autoassault.exe`
     - `clonebase.wad`
     - GLM files (game data archives)

3. **.NET 8.0 Runtime**
   - Ensure .NET 8.0 runtime is installed (should already be installed if you compiled)

## Setup Steps

### 1. Database Setup

Create three MySQL databases. You have two options:

#### Option A: Use the PowerShell Script (Recommended)

Run the provided initialization script:

```powershell
cd scripts
.\init-databases.ps1 -MySQLUser root -MySQLPassword YOUR_PASSWORD
```

If your MySQL root user has no password:
```powershell
.\init-databases.ps1 -MySQLUser root
```

If MySQL is not in your PATH, specify the path:
```powershell
.\init-databases.ps1 -MySQLUser root -MySQLPath "C:\Program Files\MariaDB 12.1\bin\mysql.exe"
```

The script will automatically check common installation locations if MySQL is not in PATH.

#### Option B: Manual SQL Creation

Connect to MySQL and run:

```sql
CREATE DATABASE autocore_auth CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE autocore_char CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE autocore_world CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

**Note:** The databases must exist before running the server. Tables will be created automatically by Entity Framework Core when the server first accesses each database context.

### 2. Configure Database Connection Strings

Edit the `appsettings.*.json` files in the output directory (typically `src/AutoCore.Launcher/bin/Debug/net8.0/`):

#### Update `appsettings.auth.json`:
```json
{
    "AuthDatabaseConnectionString": "Server=localhost;Port=3306;Database=autocore_auth;User=root;Password=YOUR_PASSWORD;Persist Security Info=False;Character Set=utf8;Connection Timeout=300"
}
```

#### Update `appsettings.global.json`:
```json
{
    "CharDatabaseConnectionString": "Server=localhost;Port=3306;Database=autocore_char;User=root;Password=YOUR_PASSWORD;Persist Security Info=False;Character Set=utf8;Connection Timeout=300",
    "WorldDatabaseConnectionString": "Server=localhost;Port=3306;Database=autocore_world;User=root;Password=YOUR_PASSWORD;Persist Security Info=False;Character Set=utf8;Connection Timeout=300"
}
```

#### Update `appsettings.sector.json`:
```json
{
    "CharDatabaseConnectionString": "Server=localhost;Port=3306;Database=autocore_char;User=root;Password=YOUR_PASSWORD;Persist Security Info=False;Character Set=utf8;Connection Timeout=300",
    "WorldDatabaseConnectionString": "Server=localhost;Port=3306;Database=autocore_world;User=root;Password=YOUR_PASSWORD;Persist Security Info=False;Character Set=utf8;Connection Timeout=300"
}
```

Replace `YOUR_PASSWORD` with your actual MySQL root password (or leave empty if no password is set).

### 3. Configure Game Path

Update the `GamePath` in both `appsettings.global.json` and `appsettings.sector.json`:

```json
{
    "GamePath": "C:\\Path\\To\\Auto Assault\\game\\Auto Assault new"
}
```

**Important:** The path must:
- Point to your Auto Assault game installation directory
- Contain `exe/autoassault.exe`
- Contain `clonebase.wad`
- Use double backslashes (`\\`) or forward slashes (`/`) in JSON

### 4. Configure Server Addresses (Optional)

If running on a different machine or need external access, update the `PublicAddress` fields:

- `appsettings.global.json`: `GameConfig.PublicAddress`
- `appsettings.sector.json`: `GameConfig.PublicAddress`

Default is `127.0.0.1` (localhost only).

## Running the Server

### Option 1: Run All Servers Together (Recommended)

Navigate to the output directory:
```powershell
cd src\AutoCore.Launcher\bin\Debug\net8.0
```

Run the launcher:
```powershell
.\AutoCore.Launcher.exe
```

This will start all three servers:
- **Auth Server** on port 2106
- **Global Server** on port 26880
- **Sector Server** on port 27001

### Option 2: Run Servers Individually

#### Auth Server:
```powershell
cd src\AutoCore.Auth\bin\Debug\net8.0
.\AutoCore.Auth.exe
```

#### Global Server:
```powershell
cd src\AutoCore.Global\bin\Debug\net8.0
.\AutoCore.Global.exe
```

#### Sector Server:
```powershell
cd src\AutoCore.Sector\bin\Debug\net8.0
.\AutoCore.Sector.exe
```

**Note:** If running individually, make sure to:
1. Start Auth Server first
2. Then start Global Server
3. Finally start Sector Server

## Port Configuration

Default ports used by the servers:
- **Auth Server**: 2106 (AuthSocketPort), 2107 (CommunicatorPort)
- **Global Server**: 26880 (GameConfig.Port), 2107 (CommunicatorPort)
- **Sector Server**: 27001 (GameConfig.Port)

Make sure these ports are not blocked by your firewall.

## Troubleshooting

### Database Connection Errors

- Verify MySQL is running: `mysql -u root -p`
- Check connection strings match your MySQL setup
- Ensure databases exist (or let EF create them on first run)
- Verify user permissions

### Game Path Errors

- Verify the path exists and contains `exe/autoassault.exe`
- Check that `clonebase.wad` exists in the game directory
- Ensure path uses correct format (double backslashes or forward slashes)

### Port Already in Use

- Check if ports 2106, 2107, 26880, or 27001 are already in use
- Change ports in the appsettings files if needed
- Ensure no other instance of the server is running

### Asset Loading Errors

- Verify game files are complete
- Check that `clonebase.wad` is not corrupted
- Ensure GLM files are present in the game directory

## Log Files

Log files are created in the same directory as the executables:
- `log-auth.txt` - Auth server logs
- `log-global.txt` - Global server logs  
- `log-sector.txt` - Sector server logs

Check these files for detailed error messages if something goes wrong.

## Default Account

When the database is first initialized, a default admin account is automatically created:

- **Username:** `admin`
- **Email:** `admin@autocore.local`
- **Password:** `admin123`
- **Level:** 255 (Admin)

**Important:** Change this password after your first login for security purposes.

You can also create additional accounts using the Auth server console command:
```
auth.create <email> <username> <password>
```

## Next Steps

Once all servers are running:
1. Verify all three servers started successfully
2. Check log files for any warnings or errors
3. Configure your Auto Assault client to connect to the server (see `CLIENT_SETUP.md`)
4. Test client connection to the server using the default account credentials
5. Create additional accounts through the Auth server console if needed

## Client Configuration

To connect the Auto Assault game client to your server, see the **[Client Setup Guide](CLIENT_SETUP.md)** for detailed instructions on configuring the client.

