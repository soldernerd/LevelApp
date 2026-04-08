namespace LevelApp.Core;

public static class AppVersion
{
    public const int Major = 0;
    public const int Minor = 5;
    public const int Patch = 9;

    public static string Full    => $"{Major}.{Minor}.{Patch}";
    public static string Display => $"v{Full}";
}
