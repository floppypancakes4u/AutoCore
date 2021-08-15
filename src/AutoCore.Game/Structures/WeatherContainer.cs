using System.Collections.Generic;

namespace AutoCore.Game.Structures
{
    public class WeatherContainer
    {
        public string Effect;
        public List<string> Environments = new();
        public List<WeatherInfo> Weathers = new();
    }
}
