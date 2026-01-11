namespace AutoCore.Game.Managers;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Packets.Login;
using AutoCore.Game.TNL;
using AutoCore.Utils.Memory;
using AutoCore.Utils.Timer;

public class LoginManager : Singleton<LoginManager>
{
    private const int SessionTimeoutCheck = 5000;
    private const int LoginTimoutInMs = 10000;
    private Dictionary<uint, GlobalLoginEntry> GlobalLogins { get; } = new();
    private Timer Timer { get; } = new();

    public LoginManager()
    {
        Timer.Add("LoginSessionExpire", SessionTimeoutCheck, true, () =>
        {
            var toRemove = new List<uint>();

            toRemove.AddRange(GlobalLogins.Where(gl => gl.Value.ExpireTime < DateTime.Now).Select(gl => gl.Key));

            lock (GlobalLogins)
            {
                foreach (var rem in toRemove)
                    GlobalLogins.Remove(rem);
            }
        });
    }

    public bool ExpectLoginToGlobal(uint accountId, string username, uint authKey)
    {
        if (string.IsNullOrEmpty(username) || authKey == 0)
        {
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"ExpectLoginToGlobal: Invalid parameters for account {accountId} (username: '{username}', authKey: {authKey})");
            return false;
        }

        lock (GlobalLogins)
        {
            if (GlobalLogins.ContainsKey(accountId))
            {
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"ExpectLoginToGlobal: Account {accountId} already has a pending login entry");
                return false;
            }

            GlobalLogins[accountId] = new GlobalLoginEntry
            {
                ExpireTime = DateTime.Now + TimeSpan.FromMilliseconds(LoginTimoutInMs),
                Username = username,
                AuthKey = authKey
            };
        }

        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"ExpectLoginToGlobal: Created login entry for account {accountId} ({username}), expires in {LoginTimoutInMs}ms");
        return true;
    }

    public void Update(long delta)
    {
        Timer.Update(delta);
    }

    public bool LoginToGlobal(TNLConnection client, LoginRequestPacket packet)
    {
        lock (GlobalLogins)
        {
            if (!GlobalLogins.TryGetValue(packet.UserId, out var entry))
            {
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"LoginToGlobal: No login entry found for account {packet.UserId} (username: '{packet.Username}')");
                return false;
            }

            if (entry.AuthKey != packet.AuthKey)
            {
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"LoginToGlobal: AuthKey mismatch for account {packet.UserId}. Expected: {entry.AuthKey}, Got: {packet.AuthKey}");
                GlobalLogins.Remove(packet.UserId);
                return false;
            }

            if (entry.Username != packet.Username)
            {
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"LoginToGlobal: Username mismatch for account {packet.UserId}. Expected: '{entry.Username}', Got: '{packet.Username}'");
                GlobalLogins.Remove(packet.UserId);
                return false;
            }

            // Remove the entry after successful validation to prevent reuse
            GlobalLogins.Remove(packet.UserId);
        }

        using var context = new CharContext();
        var account = context.Accounts.FirstOrDefault(a => a.Id == packet.UserId);
        if (account == null)
        {
            account = new Account()
            {
                Id = packet.UserId,
                Name = packet.Username,
                Level = 0,
                FirstFlags1 = 0,
                FirstFlags2 = 0,
                FirstFlags3 = 0,
                FirstFlags4 = 0
            };

            context.Accounts.Add(account);
            context.SaveChanges();
        }

        client.Account = account;

        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"LoginToGlobal: Successfully authenticated account {packet.UserId} ({packet.Username})");
        return true;
    }

    public bool LoginToSector(TNLConnection client, uint accountId)
    {
        // TODO: have some communicator register logins that will be incoming
        // and validate the current login against it

        using var context = new CharContext();
        var account = context.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null)
        {
            account = new Account()
            {
                Id = accountId,
                Name = "",
                Level = 10,
                FirstFlags1 = 0,
                FirstFlags2 = 0,
                FirstFlags3 = 0,
                FirstFlags4 = 0
            };

            context.Accounts.Add(account);
            context.SaveChanges();
        }

        client.Account = account;

        return true;
    }

    private class GlobalLoginEntry
    {
        public DateTime ExpireTime { get; set; }
        public string Username { get; set; }
        public uint AuthKey { get; set; }
    }
}
