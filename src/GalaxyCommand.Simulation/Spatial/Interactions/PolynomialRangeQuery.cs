using System.Numerics;

namespace GalaxyCommand.Simulation;

/// <summary>Exact bounded contact search for a degree-at-most-four squared-range predicate.</summary>
internal static class PolynomialRangeQuery
{
    /// <summary>
    /// Finds the first point where a constant-first polynomial is nonpositive.
    /// Coefficients use milliseconds relative to origin. Root counting retains
    /// even-multiplicity tangencies and between-millisecond contact without
    /// floating-point tolerances or scanning every millisecond.
    /// </summary>
    internal static SweptRangeContact? FindFirstContact(IEnumerable<BigInteger> coefficients,
        SimulationTime origin, SimulationTime from, SimulationTime through)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        ArgumentOutOfRangeException.ThrowIfLessThan(from.Milliseconds, origin.Milliseconds, nameof(from));
        ArgumentOutOfRangeException.ThrowIfLessThan(through.Milliseconds, from.Milliseconds, nameof(through));
        return FindFirstContact(coefficients, origin,
            InteractionFraction.Integer(from.Milliseconds - origin.Milliseconds),
            InteractionFraction.Integer(through.Milliseconds - origin.Milliseconds));
    }

    /// <summary>Searches exact fractional offsets so braking cutoffs cannot admit roots beyond the target point.</summary>
    internal static SweptRangeContact? FindFirstContact(IEnumerable<BigInteger> coefficients,
        SimulationTime origin, InteractionFraction from, InteractionFraction through, bool includeThrough = true)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (from < InteractionFraction.Integer(0) || through < from)
            throw new ArgumentOutOfRangeException(nameof(through), "Offsets must be nonnegative and ordered.");
        ulong end = through.Ceiling();
        _ = checked(origin.Milliseconds + end);
        BigInteger[] input = coefficients.ToArray();
        if (input.Length == 0 || input.Length > 5)
            throw new ArgumentException("A contact predicate requires one through five coefficients.", nameof(coefficients));
        BigInteger[] polynomial = Normalize(input);
        if (!includeThrough && from == through) return null;
        bool already = SignAt(polynomial, from) <= 0;
        if (!already && (polynomial.Length == 1 || from == through)) return null;
        ulong firstInteger = from.Ceiling();
        ulong due;
        if (already) due = firstInteger;
        else
        {
            List<BigInteger[]> sturm = SturmSequence(polynomial);
            int initial = Variations(sturm, from);
            int roots = initial - Variations(sturm, through);
            if (!includeThrough && SignAt(polynomial, through) == 0) roots--;
            if (roots == 0) return null;

            // Search integer enclosures of roots, including roots before the
            // first integer after a fractional interval start.
            ulong low = firstInteger;
            ulong high = end;
            while (low < high)
            {
                ulong middle = low + ((high - low) >> 1);
                InteractionFraction bound = InteractionFraction.Integer(middle);
                if (bound > through) bound = through;
                if (initial > Variations(sturm, bound)) high = middle;
                else low = middle + 1;
            }
            due = low;
        }

        return new SweptRangeContact(new SimulationTime(checked(origin.Milliseconds + due)),
            SignAt(polynomial, InteractionFraction.Integer(due)) <= 0, already);
    }


    /// <summary>Builds a sign-preserving Sturm chain, including repeated-root polynomials.</summary>
    private static List<BigInteger[]> SturmSequence(BigInteger[] polynomial)
    {
        BigInteger[] derivative = Normalize(polynomial.Skip(1).Select((value, index) => value * (index + 1)).ToArray());
        var sequence = new List<BigInteger[]> { polynomial, derivative };
        while (sequence[^1].Length > 1)
        {
            BigInteger[] remainder = Remainder(sequence[^2], sequence[^1]);
            if (remainder.Length == 1 && remainder[0].IsZero) break;
            sequence.Add(Normalize(remainder.Select(value => -value).ToArray()));
        }
        return sequence;
    }

    /// <summary>
    /// Computes a positive multiple of the exact division remainder. Positive
    /// scaling is essential: a negative divisor leading coefficient must not
    /// reverse the signs that Sturm root counting relies on.
    /// </summary>
    private static BigInteger[] Remainder(BigInteger[] dividend, BigInteger[] divisor)
    {
        BigInteger[] remainder = (BigInteger[])dividend.Clone();
        BigInteger leading = divisor[^1];
        while (remainder.Length >= divisor.Length && !(remainder.Length == 1 && remainder[0].IsZero))
        {
            int offset = remainder.Length - divisor.Length;
            BigInteger factor = remainder[^1] * leading.Sign;
            BigInteger scale = BigInteger.Abs(leading);
            for (int index = 0; index < remainder.Length; index++) remainder[index] *= scale;
            for (int index = 0; index < divisor.Length; index++) remainder[index + offset] -= factor * divisor[index];
            remainder = Normalize(remainder);
        }
        return remainder;
    }

    /// <summary>Removes trailing zeros and positive common factors without altering signs or roots.</summary>
    private static BigInteger[] Normalize(BigInteger[] coefficients)
    {
        int length = coefficients.Length;
        while (length > 1 && coefficients[length - 1].IsZero) length--;
        BigInteger common = BigInteger.Zero;
        for (int index = 0; index < length; index++)
            common = BigInteger.GreatestCommonDivisor(common, BigInteger.Abs(coefficients[index]));
        if (common.IsZero) return [BigInteger.Zero];
        return coefficients.Take(length).Select(value => value / common).ToArray();
    }

    /// <summary>Uses right-limit signs to include right-endpoint roots, including common repeated roots.</summary>
    private static int Variations(IReadOnlyList<BigInteger[]> sequence, InteractionFraction time)
    {
        int previous = 0;
        int variations = 0;
        foreach (BigInteger[] polynomial in sequence)
        {
            int sign = SignAt(polynomial, time);
            // At a common repeated root every chain member can vanish. Right
            // limits retain the correct variation count instead of dropping
            // the entire chain and counting unrelated later roots.
            BigInteger[] derivative = polynomial;
            while (sign == 0 && derivative.Length > 1)
            {
                derivative = derivative.Skip(1).Select((value, index) => value * (index + 1)).ToArray();
                sign = SignAt(derivative, time);
            }
            if (sign == 0) continue;
            if (previous != 0 && previous != sign) variations++;
            previous = sign;
        }
        return variations;
    }

    /// <summary>Evaluates a polynomial's sign exactly across the full simulation timeline.</summary>
    private static int SignAt(BigInteger[] polynomial, InteractionFraction time)
    {
        BigInteger value = polynomial[^1];
        BigInteger denominatorPower = time.Denominator;
        for (int index = polynomial.Length - 2; index >= 0; index--)
        {
            value = value * time.Numerator + polynomial[index] * denominatorPower;
            denominatorPower *= time.Denominator;
        }
        return value.Sign;
    }
}
