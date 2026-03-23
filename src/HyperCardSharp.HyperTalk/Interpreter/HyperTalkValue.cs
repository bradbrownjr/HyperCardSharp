using System.Globalization;

namespace HyperCardSharp.HyperTalk.Interpreter;

/// <summary>
/// Represents a HyperTalk runtime value. Everything is a string at runtime, with numeric coercion on demand.
/// </summary>
public class HyperTalkValue
{
    public static readonly HyperTalkValue Empty = new("");
    public static readonly HyperTalkValue True  = new("true");
    public static readonly HyperTalkValue False = new("false");

    public string Raw { get; }

    public HyperTalkValue(string raw) => Raw = raw;

    // ── Coercions ─────────────────────────────────────────────────────────────

    public bool AsBoolean() =>
        string.Equals(Raw, "true", StringComparison.OrdinalIgnoreCase);

    public double AsNumber()
    {
        TryAsNumber(out double d);
        return d;
    }

    public bool TryAsNumber(out double d) =>
        double.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    // ── Operators ─────────────────────────────────────────────────────────────

    public static HyperTalkValue Concat(HyperTalkValue a, HyperTalkValue b) =>
        new(a.Raw + b.Raw);

    public static HyperTalkValue ConcatSpace(HyperTalkValue a, HyperTalkValue b) =>
        new(a.Raw + " " + b.Raw);

    public static HyperTalkValue Add(HyperTalkValue a, HyperTalkValue b) =>
        new(FormatNumber(a.AsNumber() + b.AsNumber()));

    public static HyperTalkValue Subtract(HyperTalkValue a, HyperTalkValue b) =>
        new(FormatNumber(a.AsNumber() - b.AsNumber()));

    public static HyperTalkValue Multiply(HyperTalkValue a, HyperTalkValue b) =>
        new(FormatNumber(a.AsNumber() * b.AsNumber()));

    public static HyperTalkValue Divide(HyperTalkValue a, HyperTalkValue b)
    {
        double divisor = b.AsNumber();
        if (divisor == 0) return new("NaN");
        return new(FormatNumber(a.AsNumber() / divisor));
    }

    public static HyperTalkValue Mod(HyperTalkValue a, HyperTalkValue b)
    {
        double divisor = b.AsNumber();
        if (divisor == 0) return new("NaN");
        return new(FormatNumber(a.AsNumber() % divisor));
    }

    public static HyperTalkValue Div(HyperTalkValue a, HyperTalkValue b)
    {
        double divisor = b.AsNumber();
        if (divisor == 0) return new("NaN");
        return new(FormatNumber(Math.Truncate(a.AsNumber() / divisor)));
    }

    public static HyperTalkValue Power(HyperTalkValue a, HyperTalkValue b) =>
        new(FormatNumber(Math.Pow(a.AsNumber(), b.AsNumber())));

    public static HyperTalkValue Negate(HyperTalkValue a) =>
        new(FormatNumber(-a.AsNumber()));

    public static HyperTalkValue Not(HyperTalkValue a) =>
        a.AsBoolean() ? False : True;

    public static HyperTalkValue Compare(HyperTalkValue a, HyperTalkValue b, string op)
    {
        // Try numeric comparison first
        if (a.TryAsNumber(out double da) && b.TryAsNumber(out double db))
        {
            bool result = op switch
            {
                "="  or "is" => da == db,
                "<>"         => da != db,
                "<"          => da < db,
                ">"          => da > db,
                "<="         => da <= db,
                ">="         => da >= db,
                _            => false,
            };
            return result ? True : False;
        }
        else
        {
            int cmp = string.Compare(a.Raw, b.Raw, StringComparison.OrdinalIgnoreCase);
            bool result = op switch
            {
                "="  or "is" => cmp == 0,
                "<>"         => cmp != 0,
                "<"          => cmp < 0,
                ">"          => cmp > 0,
                "<="         => cmp <= 0,
                ">="         => cmp >= 0,
                _            => false,
            };
            return result ? True : False;
        }
    }

    public static HyperTalkValue Contains(HyperTalkValue container, HyperTalkValue item) =>
        container.Raw.Contains(item.Raw, StringComparison.OrdinalIgnoreCase) ? True : False;

    private static string FormatNumber(double d)
    {
        // HyperTalk shows integers without decimal point
        if (d == Math.Truncate(d) && !double.IsInfinity(d) && !double.IsNaN(d))
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("G", CultureInfo.InvariantCulture);
    }

    public override string ToString() => Raw;

    public override bool Equals(object? obj) =>
        obj is HyperTalkValue other &&
        string.Equals(Raw, other.Raw, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        Raw.ToLowerInvariant().GetHashCode();
}
