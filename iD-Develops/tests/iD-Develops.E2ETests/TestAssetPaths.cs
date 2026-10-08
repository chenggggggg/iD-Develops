namespace iD_Develops.E2ETests;

internal static class TestAssetPaths
{
    public static string JavaScript(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "wwwroot", "js", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate wwwroot/js/{fileName} from the test output directory.");
    }
}
