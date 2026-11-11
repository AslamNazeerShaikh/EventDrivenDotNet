namespace EventDrivenDesign.Shared;

/// <summary>
/// The outcome of an operation that can fail without throwing.
/// </summary>
/// <typeparam name="T">The type of the value carried on success.</typeparam>
/// <param name="IsSuccess">True when the operation succeeded.</param>
/// <param name="Value">The value produced on success.</param>
/// <param name="Error">The failure reason on failure.</param>
public readonly record struct Result<T>(bool IsSuccess, T? Value, string? Error)
{
    /// <summary>
    /// Creates a successful result carrying the given value.
    /// </summary>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Ok(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed result carrying the given error message.
    /// </summary>
    /// <param name="error">The failure reason. Must not be empty.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="error"/> is empty.</exception>
    public static Result<T> Fail(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(false, default, error);
    }
}
