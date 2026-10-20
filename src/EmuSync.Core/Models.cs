namespace EmuSync.Core;

public record Game(string Id, string Title, string FileName, long Size, string? Icon, string? Background);
public record FolderEntry(string Name, bool IsDirectory, long Size);
public interface ISyncPeer : IDisposable
{
    Task<List<Game>> Games();
    Task<(string Path,string Hash)> DownloadGame(Game game,string cache,Action<string> progress);
    Task<SaveDescriptor> Describe(string id);
    Task<SaveBundle> DownloadSave(string id,string hash);
    Task UploadSave(string id,SaveBundle bundle,string? expected);
}
public interface IGameFolder
{
    Task<IReadOnlyList<FolderEntry>> List(string path = "");
    Task<Stream> Read(string path);
    Task Write(string path, Stream data);
    Task Delete(string path);
}

public static class SafePath
{
    public static string Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                System.Text.RegularExpressions.Regex.IsMatch(p,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)",System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Invalid file path.");
        return path;
    }
    public static string GameId(string id) => System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Z]{4}[0-9]{5}$")
        ? id : throw new InvalidDataException("This game has an invalid PSP game ID.");
}
