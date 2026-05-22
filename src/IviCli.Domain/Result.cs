namespace IviCli.Domain;

/// <summary>
/// Represents the outcome of an operation that may fail with a typed error.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
/// <typeparam name="TError">The error type.</typeparam>
public abstract record Result<T, TError>
{
    private Result() { }

    /// <summary>A successful outcome carrying a value of type <typeparamref name="T"/>.</summary>
    public sealed record Ok(T Value) : Result<T, TError>;

    /// <summary>A failed outcome carrying an error of type <typeparamref name="TError"/>.</summary>
    public sealed record Error(TError Err) : Result<T, TError>;
}

/// <summary>
/// Factory methods for <see cref="Result{T, TError}"/>. Use these in preference to
/// constructing the variants directly so that type inference works at call sites.
/// </summary>
public static class Result
{
    /// <summary>Creates a successful <see cref="Result{T, TError}"/>.</summary>
    public static Result<T, TError> Success<T, TError>(T value) => new Result<T, TError>.Ok(value);

    /// <summary>Creates a failed <see cref="Result{T, TError}"/>.</summary>
    public static Result<T, TError> Failure<T, TError>(TError err) =>
        new Result<T, TError>.Error(err);
}
