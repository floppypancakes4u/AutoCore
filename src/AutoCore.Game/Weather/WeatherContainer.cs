namespace AutoCore.Game.Weather;

public class WeatherContainer
{
    public string Effect { get; set; }
    public List<string> Environments { get; } = new();
    public List<WeatherInfo> Weathers { get; } = new();
}
