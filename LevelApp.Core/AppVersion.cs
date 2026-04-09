namespace LevelApp.Core;

public static class AppVersion
{
    public const int Major = 0;
    public const int Minor = 6;
    public const int Patch = 1;

    public static string Full    => $"{Major}.{Minor}.{Patch}";
    public static string Display => $"v{Full}";
}
