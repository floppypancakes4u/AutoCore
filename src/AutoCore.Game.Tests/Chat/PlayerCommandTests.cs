using AutoCore.Database.Auth;
using AutoCore.Database.Auth.Models;
using AutoCore.Game.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

[TestClass]
public class PlayerCommandTests
{
    private string _dbName = null!;

    [TestInitialize]
    public void Init()
    {
        _dbName = "player-cmd-" + Guid.NewGuid().ToString("N");
        PlayerAccountService.Instance.CreateContext = CreateContext;
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlayerAccountService.Instance.ResetForTests();
    }

    private AuthContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AuthContext(options);
    }

    [TestMethod]
    public void AddPlayer_MissingArgs_ReturnsUsage()
    {
        // Prefer /addplayer — client treats /player as //playerrename and blocks it.
        var missing = ChatCommandService.Instance.Execute(null, "/addplayer");
        var userOnly = ChatCommandService.Instance.Execute(null, "/addplayer bob");

        Assert.IsTrue(missing.Handled);
        StringAssert.Contains(missing.Message, "Usage:");
        StringAssert.Contains(missing.Message, "/addplayer");
        Assert.IsTrue(userOnly.Handled);
        StringAssert.Contains(userOnly.Message, "Usage:");
    }

    [TestMethod]
    public void AddPlayer_CreatesAccountWithUsernameAndPassword()
    {
        var result = ChatCommandService.Instance.Execute(null, "/addplayer testuser secret123");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "testuser");
        StringAssert.Contains(result.Message.ToLowerInvariant(), "created");

        using var verify = CreateContext();
        var account = verify.Accounts.Single(a => a.Username == "testuser");
        Assert.IsTrue(account.CheckPassword("secret123"));
        Assert.AreEqual("testuser@autocore.local", account.Email);
        Assert.IsTrue(account.Validated);
        Assert.IsFalse(account.Locked);
    }

    [TestMethod]
    public void AddPlayer_DuplicateUsername_ReturnsError()
    {
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, "/addplayer dupe pass1").Handled);

        var second = ChatCommandService.Instance.Execute(null, "/addplayer dupe pass2");

        Assert.IsTrue(second.Handled);
        StringAssert.Contains(second.Message.ToLowerInvariant(), "already");
        Assert.AreEqual(1, CreateContext().Accounts.Count(a => a.Username == "dupe"));
    }

    [TestMethod]
    public void AddPlayer_UsernameTooLong_ReturnsError()
    {
        // Login packet username field is 14 bytes max.
        var result = ChatCommandService.Instance.Execute(null, "/addplayer fifteencharssss pass");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message.ToLowerInvariant(), "14");
        Assert.AreEqual(0, CreateContext().Accounts.Count());
    }

    [TestMethod]
    public void AddPlayer_PasswordTooLong_ReturnsError()
    {
        // Login packet password field is 16 bytes max.
        var result = ChatCommandService.Instance.Execute(null, "/addplayer short seventeencharspass");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message.ToLowerInvariant(), "16");
        Assert.AreEqual(0, CreateContext().Accounts.Count());
    }

    [TestMethod]
    public void AddPlayer_AliasesAndCaseInsensitive_AreHandled()
    {
        var add = ChatCommandService.Instance.Execute(null, "/ADDPLAYER CaseUser passok");
        var legacy = ChatCommandService.Instance.Execute(null, "/Player LegacyUser passok");
        var account = ChatCommandService.Instance.Execute(null, "/newaccount AccUser passok");

        Assert.IsTrue(add.Handled);
        Assert.IsTrue(legacy.Handled);
        Assert.IsTrue(account.Handled);
        using var verify = CreateContext();
        Assert.IsTrue(verify.Accounts.Any(a => a.Username == "CaseUser"));
        Assert.IsTrue(verify.Accounts.Any(a => a.Username == "LegacyUser"));
        Assert.IsTrue(verify.Accounts.Any(a => a.Username == "AccUser"));
    }

    [TestMethod]
    public void Create_EmptyUsernameOrPassword_ReturnsUsage()
    {
        var emptyUser = PlayerAccountService.Instance.Create("", "pass");
        var emptyPass = PlayerAccountService.Instance.Create("user", "   ");
        var whitespaceUser = PlayerAccountService.Instance.Create("  ", "pass");

        Assert.IsFalse(emptyUser.Success);
        StringAssert.Contains(emptyUser.Message, "Usage:");
        Assert.IsFalse(emptyPass.Success);
        StringAssert.Contains(emptyPass.Message, "Usage:");
        Assert.IsFalse(whitespaceUser.Success);
        StringAssert.Contains(whitespaceUser.Message, "Usage:");
    }

    [TestMethod]
    public void Create_MissingAuthConfiguration_ReturnsHelpfulError()
    {
        // Use the production default factory with no AuthContext.ConnectionString set.
        PlayerAccountService.Instance.ResetForTests();

        var result = PlayerAccountService.Instance.Create("cfguser", "cfgpass");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "Auth database is not configured");
    }

    [TestMethod]
    public void Create_UnexpectedFailure_ReturnsGenericError()
    {
        PlayerAccountService.Instance.CreateContext = () =>
            throw new InvalidOperationException("disk full");

        var result = PlayerAccountService.Instance.Create("failuser", "failpass");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "Failed to create player account 'failuser'");
    }
}
