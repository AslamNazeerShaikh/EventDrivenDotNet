using System.Text;

namespace EventDrivenDesign.Shared;

public enum ErrorKind { Invalid, NotFound, Unavailable }

public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Invalid);

public readonly record struct Result
{
    public Error? Error { get; private init; }

    public bool IsSuccess => Error is null;

    public static Result Success() => default;

    public static Result Failure(Error error) => new() { Error = error };

    public static implicit operator Result(Error error) => Failure(error);
}

public readonly record struct Result<T>
{
    private T? _value { get; init; }

    public Error? Error { get; private init; }

    public bool IsSuccess => Error is null;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result failed: {Error!.Code}.");

    // The synthesised PrintMembers would read Value, which throws on a failure.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"IsSuccess = {IsSuccess}, Error = {Error}");
        return true;
    }

    public static Result<T> Success(T value) => new() { _value = value };

    public static Result<T> Failure(Error error) => new() { Error = error };

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> fail) => IsSuccess ? ok(_value!) : fail(Error!);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
