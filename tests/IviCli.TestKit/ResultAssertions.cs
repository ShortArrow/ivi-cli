using IviCli.Domain;
using Shouldly;

namespace IviCli.TestKit;

/// <summary>
/// Shouldly-style assertions for <see cref="Result{T, TError}"/> in tests.
/// </summary>
public static class ResultAssertions
{
    /// <summary>
    /// Asserts the result is <see cref="Result{T, TError}.Ok"/> and returns the contained value.
    /// </summary>
    public static T ShouldBeOk<T, TError>(this Result<T, TError> result) =>
        result switch
        {
            Result<T, TError>.Ok ok => ok.Value,
            Result<T, TError>.Error err => throw new ShouldAssertException(
                $"Expected Ok but got Error({err.Err})"
            ),
            _ => throw new InvalidOperationException("Unknown Result variant"),
        };

    /// <summary>
    /// Asserts the result is <see cref="Result{T, TError}.Error"/> and returns the contained error.
    /// </summary>
    public static TError ShouldBeError<T, TError>(this Result<T, TError> result) =>
        result switch
        {
            Result<T, TError>.Error err => err.Err,
            Result<T, TError>.Ok ok => throw new ShouldAssertException(
                $"Expected Error but got Ok({ok.Value})"
            ),
            _ => throw new InvalidOperationException("Unknown Result variant"),
        };
}
