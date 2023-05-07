namespace AutoCore.Game.Structures;

using AutoCore.Utils.Extensions;

public class Music
{
    public string Name { get; set; }
    public bool Looping { get; set; }
    public bool SilenceAtMaxRadius { get; set; }
    public float DurationForRepeat { get; set; }
    public float FadeInTime { get; set; }
    public float FadeOutTime { get; set; }
    public float MaxRadius { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public MusicType Type { get; set; }

    public static Music Read(BinaryReader reader, int mapVersion)
    {
        var music = new Music();

        if (mapVersion >= 42)
        {
            music.Name = reader.ReadLengthedString();
            music.Looping = reader.ReadBoolean();
            music.SilenceAtMaxRadius = reader.ReadBoolean();
            music.DurationForRepeat = reader.ReadSingle();
            music.FadeInTime = reader.ReadSingle();
            music.FadeOutTime = reader.ReadSingle();
            music.MaxRadius = reader.ReadSingle();

            if (music.MaxRadius <= 0.0f)
                music.MaxRadius = 10.0f;

            music.X = reader.ReadSingle();
            music.Y = reader.ReadSingle();
            music.Z = reader.ReadSingle();
            music.Type = (MusicType)reader.ReadInt32();
        }

        return music;
    }

    public enum MusicType
    {
        Unknown = 0,
        Foreground = 1,
        Backround = 2,
        Loading = 3,
        Reaction = 4,
        Default = 5
    }
}
