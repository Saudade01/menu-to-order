namespace MenuToOrder.Tests;

internal static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MenuToOrder.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root above the test binaries.");
    }
}
