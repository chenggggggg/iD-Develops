namespace iD_Develops.E2ETests;

internal static class FileMailTestStore
{
    private static readonly object Gate = new();

    public static List<(string Path, string Body)> Matching(string directory, string marker)
    {
        lock (Gate) return ReadCompleted(directory, marker);
    }

    public static void DeleteMatching(string directory, string marker)
    {
        lock (Gate)
            foreach (var mail in ReadCompleted(directory, marker)) File.Delete(mail.Path);
    }

    private static List<(string Path, string Body)> ReadCompleted(string directory, string marker)
    {
        var matches = new List<(string Path, string Body)>();
        if (!Directory.Exists(directory)) return matches;
        foreach (var path in Directory.EnumerateFiles(directory, "*.html"))
        {
            string body;
            try { body = File.ReadAllText(path); }
            catch (IOException exception) when (exception is FileNotFoundException ||
                (exception.HResult & 0xffff) is 32 or 33)
            {
                // Other requests can still be writing their own messages. Callers
                // retain their exact delivery-count and content assertions.
                continue;
            }
            if (body.Contains(marker, StringComparison.Ordinal)) matches.Add((path, body));
        }
        return matches;
    }
}
