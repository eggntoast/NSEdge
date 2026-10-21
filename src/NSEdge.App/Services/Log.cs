using System.IO;
namespace NSEdge.App.Services;

public static class Log
{
    private static readonly object Gate = new();
    private static readonly string Pathname = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NSEdge", "NSEdge.log");

    public static string Path => Pathname;

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Pathname)!);
                if (File.Exists(Pathname) && new FileInfo(Pathname).Length > 2_000_000)
                    File.Move(Pathname, Pathname + ".old", overwrite: true);
                File.AppendAllText(Pathname, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
