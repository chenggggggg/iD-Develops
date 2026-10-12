namespace iD_Develops.E2ETests;

internal static class TestAssetPaths
{
    public static string JavaScript(string fileName)
        => Asset("js", fileName);

    public static string Css(string fileName)
        => Asset("css", fileName);

    private static string Asset(string directoryName, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "wwwroot", directoryName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate wwwroot/{directoryName}/{fileName} from the test output directory.");
    }
}
