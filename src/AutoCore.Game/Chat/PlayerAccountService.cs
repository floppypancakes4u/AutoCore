using AutoCore.Database.Auth;
using AutoCore.Database.Auth.Models;

namespace AutoCore.Game.Chat;

/// <summary>
/// Creates auth login accounts for quick player setup (chat <c>/addplayer</c> command).
/// Char-side account rows are still created on first login by <c>LoginManager</c>.
/// </summary>
public sealed class PlayerAccountService
{
    /// <summary>Login packet username field is 14 bytes.</summary>
    public const int MaxUsernameLength = 14;

    /// <summary>Login packet password field is 16 bytes.</summary>
    public const int MaxPasswordLength = 16;

    private static readonly Func<AuthContext> DefaultCreateContext = static () => new AuthContext();

    public static PlayerAccountService Instance { get; } = new();

    /// <summary>Factory for short-lived contexts (tests inject InMemory contexts).</summary>
    internal Func<AuthContext> CreateContext { get; set; } = DefaultCreateContext;

    /// <summary>Restore default factory after tests.</summary>
    internal void ResetForTests() => CreateContext = DefaultCreateContext;

    public PlayerAccountCreateResult Create(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new PlayerAccountCreateResult(false, "Usage: /addplayer <user> <pass>");

        username = username.Trim();
        password = password.Trim();

        if (username.Length > MaxUsernameLength)
            return new PlayerAccountCreateResult(false, $"Username must be at most {MaxUsernameLength} characters (login limit).");

        if (password.Length > MaxPasswordLength)
            return new PlayerAccountCreateResult(false, $"Password must be at most {MaxPasswordLength} characters (login limit).");

        if (ReferenceEquals(CreateContext, DefaultCreateContext)
            && string.IsNullOrEmpty(AuthContext.ConnectionString))
        {
            return new PlayerAccountCreateResult(
                false,
                "Auth database is not configured. Set AuthDatabaseConnectionString on the sector/launcher config.");
        }

        try
        {
            using var context = CreateContext();

            if (context.Accounts.Any(a => a.Username == username))
                return new PlayerAccountCreateResult(false, $"Username '{username}' is already taken.");

            var salt = Account.CreateSalt();
            context.Accounts.Add(new Account
            {
                Email = $"{username}@autocore.local",
                Username = username,
                Password = Account.Hash(password, salt),
                Salt = salt,
                JoinDate = DateTime.Now,
                Validated = true,
                Locked = false,
                Level = 0
            });
            context.SaveChanges();

            return new PlayerAccountCreateResult(true, $"Created player account '{username}'.");
        }
        catch (Exception)
        {
            return new PlayerAccountCreateResult(false, $"Failed to create player account '{username}'.");
        }
    }
}

public readonly struct PlayerAccountCreateResult
{
    public PlayerAccountCreateResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }
    public string Message { get; }
}
