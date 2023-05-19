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
        if (GlobalLogins.ContainsKey(accountId) || string.IsNullOrEmpty(username) || authKey == 0)
            return false;

        lock (GlobalLogins)
        {
            GlobalLogins[accountId] = new GlobalLoginEntry
            {
                ExpireTime = DateTime.Now + TimeSpan.FromMilliseconds(LoginTimoutInMs),
                Username = username,
                AuthKey = authKey
            };
        }

        return true;
    }

    public void Update(long delta)
    {
        Timer.Update(delta);
    }

    public bool LoginToGlobal(TNLConnection client, LoginRequestPacket packet)
    {
        if (!GlobalLogins.TryGetValue(packet.UserId, out var entry))
            return false;

        if (entry.AuthKey != packet.AuthKey || entry.Username != packet.Username)
            return false;

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

        return true;
    }

    private class GlobalLoginEntry
    {
        public DateTime ExpireTime { get; set; }
        public string Username { get; set; }
        public uint AuthKey { get; set; }
    }
}
