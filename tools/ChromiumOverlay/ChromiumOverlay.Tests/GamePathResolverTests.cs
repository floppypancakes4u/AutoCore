using ChromiumOverlay;

namespace ChromiumOverlay.Tests;

public class GamePathResolverTests
{
    private const string DefaultInstall = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    [Fact]
    public void ResolveExe_PrefersExplicitExePath()
    {
        var options = new GameLaunchOptions
        {
            ExplicitExePath = @"D:\games\autoassault.exe",
            GamePath = @"C:\ignored",
            EnvironmentInstall = @"E:\also-ignored",
        };

        var resolved = GamePathResolver.ResolveExePath(options);

        Assert.Equal(@"D:\games\autoassault.exe", resolved);
    }

    [Fact]
    public void ResolveExe_UsesGamePathWhenNoExplicitExe()
    {
        var options = new GameLaunchOptions
        {
            GamePath = @"D:\Auto Assault",
            EnvironmentInstall = @"E:\ignored",
        };

        var resolved = GamePathResolver.ResolveExePath(options);

        Assert.Equal(Path.Combine(@"D:\Auto Assault", "exe", "autoassault.exe"), resolved);
    }

    [Fact]
    public void ResolveExe_UsesAaInstallEnvWhenNoGamePath()
    {
        var options = new GameLaunchOptions
        {
            EnvironmentInstall = @"F:\AA",
        };

        var resolved = GamePathResolver.ResolveExePath(options);

        Assert.Equal(Path.Combine(@"F:\AA", "exe", "autoassault.exe"), resolved);
    }

    [Fact]
    public void ResolveExe_DefaultRoot_PrefersBakWhenPresentElseStock()
    {
        var options = new GameLaunchOptions();
        var resolved = GamePathResolver.ResolveExePath(options);
        var expectedRoot = File.Exists(Path.Combine(GamePathResolver.DefaultBakInstall, "exe", "autoassault.exe"))
            ? GamePathResolver.DefaultBakInstall
            : GamePathResolver.DefaultInstall;

        Assert.Equal(Path.Combine(expectedRoot, "exe", "autoassault.exe"), resolved);
    }

    [Fact]
    public void ResolveDefaultInstallRoot_ReturnsBakConstant_WhenBakExeExistsOnDisk()
    {
        var bakExe = Path.Combine(GamePathResolver.DefaultBakInstall, "exe", "autoassault.exe");
        if (!File.Exists(bakExe))
            return; // machine without RE copy

        Assert.Equal(GamePathResolver.DefaultBakInstall, GamePathResolver.ResolveDefaultInstallRoot());
    }

    [Fact]
    public void BuildStartInfo_UsesDeveloperFlagAndExeWorkingDirectory()
    {
        var exe = Path.Combine(@"D:\Auto Assault", "exe", "autoassault.exe");
        var info = GamePathResolver.BuildStartInfo(exe);

        Assert.Equal(exe, info.FileName);
        Assert.Equal("-developer", info.Arguments);
        Assert.Equal(Path.GetDirectoryName(exe), info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
    }
}
