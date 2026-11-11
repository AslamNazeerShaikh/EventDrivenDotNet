namespace EventDrivenDesign.Shared;

/// <summary>
/// Precondition checks that throw instead of returning <see cref="Result{T}"/>.
/// </summary>
public static class Guard
{
    /// <summary>
    /// Throws when the value is negative.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The value, for fluent use.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static int AgainstNegative(int value, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
        return value;
    }

    /// <summary>
    /// Throws when the string is null, empty or whitespace.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <returns>The value, for fluent use.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public static string AgainstEmpty(string? value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }
}
