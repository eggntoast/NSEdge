using System.Globalization;

namespace NSEdge.Core;

public readonly record struct LightbarColor(byte Red, byte Green, byte Blue)
{
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public static bool TryParse(string? value, out LightbarColor color)
    {
        color = default;
        if (value is null || value.Length != 7 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return false;
        color = new LightbarColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static LightbarColor Blend(LightbarColor low, LightbarColor high, double weight)
    {
        double t = Math.Clamp(weight, 0, 1);
        return new LightbarColor(
            (byte)Math.Round(low.Red + (high.Red - low.Red) * t),
            (byte)Math.Round(low.Green + (high.Green - low.Green) * t),
            (byte)Math.Round(low.Blue + (high.Blue - low.Blue) * t));
    }
}

public sealed record LightbarPalette
{
    public LightbarColor Critical { get; init; } = new(239, 70, 87);
    public LightbarColor Low { get; init; } = new(245, 132, 54);
    public LightbarColor Medium { get; init; } = new(242, 186, 66);
    public LightbarColor High { get; init; } = new(38, 206, 120);
    public LightbarColor Charging { get; init; } = new(73, 157, 255);
    public LightbarColor Full { get; init; } = new(38, 206, 120);
    public LightbarColor Static { get; init; } = new(117, 184, 255);
    public int CriticalBelowPercent { get; init; } = 20;
    public int LowBelowPercent { get; init; } = 40;
    public int MediumBelowPercent { get; init; } = 70;

    public bool HasValidThresholds =>
        CriticalBelowPercent >= 10 && CriticalBelowPercent < LowBelowPercent &&
        LowBelowPercent < MediumBelowPercent && MediumBelowPercent <= 100;
}

public enum LightbarMode { BatteryBands, BatteryGradient, Static }

public static class LightbarPolicy
{
    public static LightbarColor? Select(EdgeBattery battery, LightbarMode mode, LightbarPalette palette)
    {
        if (mode == LightbarMode.Static)
            return palette.Static;
        if (!palette.HasValidThresholds || battery.Decile is null)
            return null;
        if (battery.Charge == EdgeChargeState.Full) return palette.Full;
        if (battery.Charge == EdgeChargeState.Charging) return palette.Charging;
        if (battery.Charge != EdgeChargeState.Discharging) return null;

        int percent = battery.Decile.Value * 10;
        if (mode == LightbarMode.BatteryBands)
        {
            if (percent < palette.CriticalBelowPercent) return palette.Critical;
            if (percent < palette.LowBelowPercent) return palette.Low;
            if (percent < palette.MediumBelowPercent) return palette.Medium;
            return palette.High;
        }

        if (percent <= palette.CriticalBelowPercent) return palette.Critical;
        if (percent < palette.LowBelowPercent)
            return LightbarColor.Blend(palette.Critical, palette.Low,
                (percent - palette.CriticalBelowPercent) / (double)(palette.LowBelowPercent - palette.CriticalBelowPercent));
        if (percent < palette.MediumBelowPercent)
            return LightbarColor.Blend(palette.Low, palette.Medium,
                (percent - palette.LowBelowPercent) / (double)(palette.MediumBelowPercent - palette.LowBelowPercent));
        return LightbarColor.Blend(palette.Medium, palette.High,
            (percent - palette.MediumBelowPercent) / (double)(100 - palette.MediumBelowPercent));
    }
}
