namespace NSEdge.Core;

public enum EdgeChargeState
{
    Unavailable,
    Discharging,
    Charging,
    Full,
    NotCharging,
    Error
}

public readonly record struct EdgeBattery(int? Decile, EdgeChargeState Charge)
{
    public static readonly EdgeBattery Unavailable = new(null, EdgeChargeState.Unavailable);

    public string LevelText => Decile switch
    {
        null => "Unavailable",
        10 => "100%",
        int level => $"{level * 10}–{level * 10 + 9}%"
    };

    public string StateText => Charge switch
    {
        EdgeChargeState.Discharging => "Discharging",
        EdgeChargeState.Charging => "Charging",
        EdgeChargeState.Full => "Fully charged",
        EdgeChargeState.NotCharging => "Not charging",
        EdgeChargeState.Error => "Battery error",
        _ => "Status unavailable"
    };

    public string Description => Decile is null ? StateText : $"{LevelText} · {StateText}";
}

public static class EdgeInputDecoder
{
    // USB report 0x01: PS is buttons[2] bit 0; status[0] is byte 53 including report ID.
    // The battery nibble reports 10% bands, not a precise percentage.
    public static bool TryDecode(ReadOnlySpan<byte> report, out EdgeBattery battery, out bool psDown)
    {
        battery = EdgeBattery.Unavailable;
        psDown = false;

        if (report.Length != 64 || report[0] != 0x01)
            return false;

        psDown = (report[10] & 0x01) != 0;
        DecodeBatteryStatus(report[53], out battery);
        return true;
    }

    public static void DecodeBatteryStatus(byte status, out EdgeBattery battery)
    {
        int decile = status & 0x0F;
        int charge = status >> 4;

        battery = charge switch
        {
            0 or 1 when decile <= 10 => new EdgeBattery(decile,
                charge == 0 ? EdgeChargeState.Discharging : EdgeChargeState.Charging),
            2 => new EdgeBattery(10, EdgeChargeState.Full),
            0xA or 0xB => new EdgeBattery(null, EdgeChargeState.NotCharging),
            0xF => new EdgeBattery(null, EdgeChargeState.Error),
            _ => EdgeBattery.Unavailable
        };
    }
}

public sealed class PsPressDetector
{
    private bool? previous;
    private DateTimeOffset lastPress = DateTimeOffset.MinValue;

    public bool Observe(bool pressed, DateTimeOffset now)
    {
        if (previous is null)
        {
            previous = pressed;
            return false;
        }

        bool rising = previous == false && pressed;
        previous = pressed;
        if (!rising || now - lastPress < TimeSpan.FromMilliseconds(300))
            return false;

        lastPress = now;
        return true;
    }
}

public sealed class LowBatteryPolicy
{
    private bool notified;

    public bool Observe(EdgeBattery battery, int thresholdPercent)
    {
        if (battery.Charge is EdgeChargeState.Charging or EdgeChargeState.Full)
        {
            notified = false;
            return false;
        }

        if (battery.Charge != EdgeChargeState.Discharging || battery.Decile is null)
            return false;

        if (battery.Decile.Value * 10 >= thresholdPercent)
        {
            notified = false;
            return false;
        }

        if (notified)
            return false;

        notified = true;
        return true;
    }

    public void Reset() => notified = false;
}
