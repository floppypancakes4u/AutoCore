using ChromiumOverlay;

namespace ChromiumOverlay.Tests;

public class PlayerCombatPoolsTests
{
    [Fact]
    public void Offsets_MatchDevToolAndBakPack()
    {
        Assert.Equal(0x91A840, PlayerCombatPools.VogClientBaseRva);
        Assert.Equal(0x0E98, PlayerCombatPools.LocalPlayerOffset);
        Assert.Equal(0x250, PlayerCombatPools.PlayerVehicleOffset);
        Assert.Equal(0x144, PlayerCombatPools.CurrentShieldOffset);
        Assert.Equal(0x148, PlayerCombatPools.MaxShieldOffset);
        Assert.Equal(0x12C, PlayerCombatPools.CurrentPowerOffset);
        Assert.Equal(0x12E, PlayerCombatPools.MaxPowerOffset);
    }

    [Fact]
    public void ToJson_And_TryParseJson_RoundTrip()
    {
        var pools = new PlayerCombatPools(
            Hp: 80, MaxHp: 100,
            Power: 40, MaxPower: 50,
            Shield: 20, MaxShield: 30,
            HasVehicle: true);

        var json = pools.ToJson(tick: 7, pid: 1234);
        Assert.Contains("\"hp\":80", json);
        Assert.Contains("\"maxHp\":100", json);
        Assert.Contains("\"power\":40", json);
        Assert.Contains("\"maxPower\":50", json);
        Assert.Contains("\"shield\":20", json);
        Assert.Contains("\"maxShield\":30", json);
        Assert.Contains("\"hasVehicle\":true", json);

        Assert.True(PlayerCombatPools.TryParseJson(json, out var parsed));
        Assert.Equal(pools, parsed with { /* tick/pid not in struct */ });
        Assert.Equal(80, parsed.Hp);
        Assert.Equal(100, parsed.MaxHp);
        Assert.Equal(40, parsed.Power);
        Assert.Equal(50, parsed.MaxPower);
        Assert.Equal(20, parsed.Shield);
        Assert.Equal(30, parsed.MaxShield);
        Assert.True(parsed.HasVehicle);
    }

    [Fact]
    public void TryParseJson_ReturnsFalse_OnGarbage()
    {
        Assert.False(PlayerCombatPools.TryParseJson("not-json", out _));
        Assert.False(PlayerCombatPools.TryParseJson("", out _));
    }

    [Fact]
    public void Empty_UsesUnknownHp()
    {
        Assert.Equal(PlayerCombatPools.Unknown, PlayerCombatPools.Empty.Hp);
        Assert.False(PlayerCombatPools.Empty.HasVehicle);
    }
}
