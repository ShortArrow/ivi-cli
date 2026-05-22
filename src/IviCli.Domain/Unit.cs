namespace IviCli.Domain;

/// <summary>
/// A unit type representing the absence of a meaningful value. Used as the
/// success payload for <see cref="Result{T, TError}"/>-returning operations
/// whose only outcome is "succeeded".
/// </summary>
public readonly record struct Unit
{
    /// <summary>The singleton <see cref="Unit"/> value.</summary>
    public static Unit Value => default;
}
