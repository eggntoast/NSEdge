namespace NSEdge.Core;

/// <summary>Demonstration contract only. Not implemented by stock DS4Windows IPC.</summary>
public static class LightingLeaseProtocol
{
    public const int Version = 1;
    public static readonly TimeSpan SuggestedLease = TimeSpan.FromSeconds(5);
}

/// <summary>In-memory representation of the *currently selected* profile and a temporary colour.</summary>
public sealed class SimulatedLightbarSink
{
    private readonly object sync = new();
    private string currentProfile = "";
    private LightbarColor profileColour;
    private LightbarColor? displayedColour;
    private bool overrideActive;

    public string CurrentProfile { get { lock (sync) return currentProfile; } }
    public LightbarColor ProfileColour { get { lock (sync) return profileColour; } }
    public LightbarColor? DisplayedColour { get { lock (sync) return displayedColour; } }
    public bool OverrideActive { get { lock (sync) return overrideActive; } }

    internal void Connect(string profile, LightbarColor colour)
    {
        lock (sync)
        {
            currentProfile = profile;
            profileColour = colour;
            overrideActive = false;
            displayedColour = colour;
        }
    }

    internal void ChangeProfile(string profile, LightbarColor colour)
    {
        lock (sync)
        {
            currentProfile = profile;
            profileColour = colour;
            if (!overrideActive) displayedColour = colour;
        }
    }

    internal void Override(LightbarColor colour)
    {
        lock (sync)
        {
            overrideActive = true;
            displayedColour = colour;
        }
    }

    internal void Restore()
    {
        lock (sync)
        {
            overrideActive = false;
            displayedColour = profileColour;
        }
    }

    internal void Disconnect()
    {
        lock (sync)
        {
            overrideActive = false;
            displayedColour = null;
        }
    }
}

/// <summary>
/// Simulation-only single-controller ownership model: no HID, DS4Windows IPC, disk,
/// threads, process access or physical output. The host must call Expire periodically.
/// </summary>
public sealed class SimulatedLightingLeaseHost
{
    private readonly object sync = new();
    private readonly TimeProvider clock;
    private readonly TimeSpan leaseDuration;
    private readonly string physicalIdentity;
    private readonly SimulatedLightbarSink sink;
    private Guid currentToken;
    private long lastRenewal;
    private bool connected;
    private long generation;

    public SimulatedLightingLeaseHost(string physicalIdentity, SimulatedLightbarSink sink,
        TimeProvider clock, TimeSpan? leaseDuration = null)
    {
        this.physicalIdentity = ControllerIdentity.Normalize(physicalIdentity)
            ?? throw new ArgumentException("Invalid controller identity.", nameof(physicalIdentity));
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.leaseDuration = leaseDuration ?? LightingLeaseProtocol.SuggestedLease;
        if (this.leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    public int ProtocolVersion => LightingLeaseProtocol.Version;
    public bool SupportsPhysicalOutput => false;
    public bool Connected { get { lock (sync) return connected; } }
    public long ConnectionGeneration { get { lock (sync) return generation; } }
    public bool HasLease
    {
        get { lock (sync) { ExpireLocked(); return currentToken != Guid.Empty; } }
    }

    public void Connect(string identity, string activeProfile, LightbarColor currentProfileColour)
    {
        lock (sync)
        {
            if (ControllerIdentity.Normalize(identity) != physicalIdentity)
                throw new InvalidOperationException("Physical controller identity does not match.");
            if (string.IsNullOrWhiteSpace(activeProfile))
                throw new ArgumentException("A profile must be selected.", nameof(activeProfile));
            ClearLeaseLocked();
            generation = checked(generation + 1);
            connected = true;
            sink.Connect(activeProfile, currentProfileColour);
        }
    }

    public void ChangeProfile(string profile, LightbarColor colour)
    {
        lock (sync)
        {
            if (!connected) throw new InvalidOperationException("Controller is disconnected.");
            if (string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("Invalid profile.", nameof(profile));
            ExpireLocked();
            sink.ChangeProfile(profile, colour);
        }
    }

    public void Disconnect()
    {
        lock (sync)
        {
            ClearLeaseLocked();
            connected = false;
            sink.Disconnect();
        }
    }

    public Guid? Acquire(string identity, long connectionGeneration)
    {
        lock (sync)
        {
            ExpireLocked();
            if (!connected || connectionGeneration != generation ||
                ControllerIdentity.Normalize(identity) != physicalIdentity || currentToken != Guid.Empty)
                return null;
            currentToken = Guid.NewGuid();
            lastRenewal = clock.GetTimestamp();
            return currentToken;
        }
    }

    public bool SetColor(Guid token, LightbarColor colour)
    {
        lock (sync)
        {
            ExpireLocked();
            if (currentToken == Guid.Empty || token != currentToken) return false;
            sink.Override(colour);
            return true;
        }
    }

    public bool Renew(Guid token)
    {
        lock (sync)
        {
            ExpireLocked();
            if (currentToken == Guid.Empty || token != currentToken) return false;
            lastRenewal = clock.GetTimestamp();
            return true;
        }
    }

    public bool Release(Guid token)
    {
        lock (sync)
        {
            ExpireLocked();
            if (currentToken == Guid.Empty || token != currentToken) return false;
            ClearLeaseLocked();
            return true;
        }
    }

    /// <summary>Only a simulated server calls this. A real host must schedule its own timer.</summary>
    public bool Expire()
    {
        lock (sync) return ExpireLocked();
    }

    private bool ExpireLocked()
    {
        if (currentToken == Guid.Empty) return false;
        if (clock.GetElapsedTime(lastRenewal, clock.GetTimestamp()) < leaseDuration) return false;
        ClearLeaseLocked();
        return true;
    }

    private void ClearLeaseLocked()
    {
        currentToken = Guid.Empty;
        if (connected) sink.Restore();
    }
}
