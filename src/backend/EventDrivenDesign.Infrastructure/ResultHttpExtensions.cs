using EventDrivenDesign.Shared;
using Microsoft.AspNetCore.Http;

namespace EventDrivenDesign.Infrastructure;

public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result, int successStatus = StatusCodes.Status202Accepted) =>
        result.Error is { } error ? Problem(error) : Results.StatusCode(successStatus);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.Match(Results.Ok, Problem);

    private static IResult Problem(Error error) => Results.Problem(
        statusCode: error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        },
        title: error.Code,
        detail: error.Message);
}
