namespace LevelApp.Core;

public static class AppVersion
{
    public const int Major = 0;
    public const int Minor = 9;
    public const int Patch = 5;

    public static string Full    => $"{Major}.{Minor}.{Patch}";
    public static string Display => $"v{Full}";
}
