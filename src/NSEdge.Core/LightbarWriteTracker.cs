namespace NSEdge.Core;

/// <summary>
/// Suppresses duplicate successful lightbar writes without hiding failed output attempts.
/// A reset also invalidates any result from an earlier controller session or configuration.
/// </summary>
public sealed class LightbarWriteTracker
{
    private readonly object sync = new();
    private string? lastSuccessfulKey;

    public bool TryWrite(string key, Action write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(write);

        lock (sync)
        {
            if (lastSuccessfulKey == key)
                return false;

            try
            {
                write();
                lastSuccessfulKey = key;
                return true;
            }
            catch
            {
                // The controller's state is unknown after a failed or partial write.
                lastSuccessfulKey = null;
                throw;
            }
        }
    }

    public void Reset()
    {
        lock (sync)
            lastSuccessfulKey = null;
    }
}
