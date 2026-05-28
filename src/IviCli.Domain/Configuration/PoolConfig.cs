namespace IviCli.Domain.Configuration;

/// <summary>
/// The <c>[pool]</c> section of a configuration document (ADR 0038).
/// Controls the backend session pool: whether it is active, how long
/// an idle entry survives, and the maximum number of cached entries.
/// </summary>
public sealed record PoolConfig
{
    /// <summary>The all-defaults pool configuration (enabled, 60s idle, 16 max).</summary>
    public static PoolConfig Default { get; } =
        new(enabled: true, idleTimeout: TimeSpan.FromSeconds(60), maxDevices: 16);

    /// <summary>Whether the pool layer is installed in the composition root.</summary>
    public bool Enabled { get; }

    /// <summary>Maximum idle duration before a cached entry is evicted.</summary>
    public TimeSpan IdleTimeout { get; }

    /// <summary>LRU upper bound on cached sessions; 0 means unlimited.</summary>
    public int MaxDevices { get; }

    private PoolConfig(bool enabled, TimeSpan idleTimeout, int maxDevices)
    {
        Enabled = enabled;
        IdleTimeout = idleTimeout;
        MaxDevices = maxDevices;
    }

    /// <summary>
    /// Validates and constructs a <see cref="PoolConfig"/>.
    /// </summary>
    public static Result<PoolConfig, PoolConfigError> From(
        bool enabled,
        TimeSpan idleTimeout,
        int maxDevices
    )
    {
        if (idleTimeout < TimeSpan.Zero)
        {
            return Result.Failure<PoolConfig, PoolConfigError>(
                new NegativeIdleTimeout(idleTimeout)
            );
        }
        if (maxDevices < 0)
        {
            return Result.Failure<PoolConfig, PoolConfigError>(new NegativeMaxDevices(maxDevices));
        }
        return Result.Success<PoolConfig, PoolConfigError>(
            new PoolConfig(enabled, idleTimeout, maxDevices)
        );
    }
}

/// <summary>Errors that can surface from <see cref="PoolConfig.From"/>.</summary>
public abstract record PoolConfigError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>The supplied <see cref="PoolConfig.IdleTimeout"/> was negative.</summary>
public sealed record NegativeIdleTimeout(TimeSpan Value) : PoolConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "pool idle_timeout must be non-negative, got {Value}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Value };
}

/// <summary>The supplied <see cref="PoolConfig.MaxDevices"/> was negative.</summary>
public sealed record NegativeMaxDevices(int Value) : PoolConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "pool max_devices must be non-negative, got {Value}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Value };
}
