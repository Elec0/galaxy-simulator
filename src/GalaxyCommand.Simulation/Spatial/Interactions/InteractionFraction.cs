using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Exact fractional milliseconds or scalar values used only during derived evaluation.</summary>
internal readonly record struct InteractionFraction : IComparable<InteractionFraction>
{
    internal InteractionFraction(BigInteger numerator, BigInteger denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        BigInteger common = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / common;
        Denominator = denominator / common;
    }

    internal BigInteger Numerator { get; }
    internal BigInteger Denominator { get; }
    internal static InteractionFraction Integer(BigInteger value) => new(value, BigInteger.One);
    internal ulong Ceiling() => checked((ulong)((Numerator + Denominator - 1) / Denominator));
    public int CompareTo(InteractionFraction other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public static InteractionFraction operator +(InteractionFraction first, InteractionFraction second) =>
        new(first.Numerator * second.Denominator + second.Numerator * first.Denominator, first.Denominator * second.Denominator);
    public static InteractionFraction operator -(InteractionFraction first, InteractionFraction second) =>
        new(first.Numerator * second.Denominator - second.Numerator * first.Denominator, first.Denominator * second.Denominator);
    public static bool operator <(InteractionFraction first, InteractionFraction second) => first.CompareTo(second) < 0;
    public static bool operator >(InteractionFraction first, InteractionFraction second) => first.CompareTo(second) > 0;
    public static bool operator <=(InteractionFraction first, InteractionFraction second) => first.CompareTo(second) <= 0;
    public static bool operator >=(InteractionFraction first, InteractionFraction second) => first.CompareTo(second) >= 0;
}
