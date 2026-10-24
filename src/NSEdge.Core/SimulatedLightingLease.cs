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
    public string CurrentProfile { get; private set; } = "";
    public LightbarColor ProfileColour { get; private set; }
    public LightbarColor? DisplayedColour { get; private set; }
    public bool OverrideActive { get; private set; }

    internal void Connect(string profile, LightbarColor colour)
    {
        CurrentProfile = profile;
        ProfileColour = colour;
        OverrideActive = false;
        DisplayedColour = colour;
    }

    internal void ChangeProfile(string profile, LightbarColor colour)
    {
        CurrentProfile = profile;
        ProfileColour = colour;
        if (!OverrideActive) DisplayedColour = colour;
    }

    internal void Override(LightbarColor colour)
    {
        OverrideActive = true;
        DisplayedColour = colour;
    }

    internal void Restore()
    {
        OverrideActive = false;
        DisplayedColour = ProfileColour;
    }

    internal void Disconnect()
    {
        OverrideActive = false;
        DisplayedColour = null;
    }
}

/// <summary>
/// Simulation-only single-controller ownership model: no HID, DS4Windows IPC, disk,
/// threads, process access or physical output. The host must call Expire periodically.
/// </summary>
public sealed class SimulatedLightingLeaseHost
{
    private readonly TimeProvider clock;
    private readonly TimeSpan leaseDuration;
    private readonly string physicalIdentity;
    private readonly SimulatedLightbarSink sink;
    private Guid currentToken;
    private long lastRenewal;
    private bool connected;

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
    public bool Connected => connected;
    public long ConnectionGeneration { get; private set; }
    public bool HasLease
    {
        get { Expire(); return currentToken != Guid.Empty; }
    }

    public void Connect(string identity, string activeProfile, LightbarColor currentProfileColour)
    {
        if (ControllerIdentity.Normalize(identity) != physicalIdentity)
            throw new InvalidOperationException("Physical controller identity does not match.");
        if (string.IsNullOrWhiteSpace(activeProfile))
            throw new ArgumentException("A profile must be selected.", nameof(activeProfile));
        ClearLease();
        ConnectionGeneration = checked(ConnectionGeneration + 1);
        connected = true;
        sink.Connect(activeProfile, currentProfileColour);
    }

    public void ChangeProfile(string profile, LightbarColor colour)
    {
        if (!connected) throw new InvalidOperationException("Controller is disconnected.");
        if (string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("Invalid profile.", nameof(profile));
        Expire();
        sink.ChangeProfile(profile, colour);
    }

    public void Disconnect()
    {
        ClearLease();
        connected = false;
        sink.Disconnect();
    }

    public Guid? Acquire(string identity, long connectionGeneration)
    {
        Expire();
        if (!connected || connectionGeneration != ConnectionGeneration ||
            ControllerIdentity.Normalize(identity) != physicalIdentity || currentToken != Guid.Empty)
            return null;
        currentToken = Guid.NewGuid();
        lastRenewal = clock.GetTimestamp();
        return currentToken;
    }

    public bool SetColor(Guid token, LightbarColor colour)
    {
        Expire();
        if (currentToken == Guid.Empty || token != currentToken) return false;
        sink.Override(colour);
        return true;
    }

    public bool Renew(Guid token)
    {
        Expire();
        if (currentToken == Guid.Empty || token != currentToken) return false;
        lastRenewal = clock.GetTimestamp();
        return true;
    }

    public bool Release(Guid token)
    {
        Expire();
        if (currentToken == Guid.Empty || token != currentToken) return false;
        ClearLease();
        return true;
    }

    /// <summary>Invoked by a future server timer; models recovery when a client disappears.</summary>
    public bool Expire()
    {
        if (currentToken == Guid.Empty) return false;
        if (clock.GetElapsedTime(lastRenewal, clock.GetTimestamp()) < leaseDuration) return false;
        ClearLease();
        return true;
    }

    private void ClearLease()
    {
        currentToken = Guid.Empty;
        if (connected) sink.Restore();
    }
}
