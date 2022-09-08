namespace AutoCore.Utils.Memory;

public abstract class Singleton<T> where T : class, new()
{
    private static readonly Lazy<T> sInstance = new();

    public static T Instance
    {
        get
        {
            return sInstance.Value;
        }
    }
}
