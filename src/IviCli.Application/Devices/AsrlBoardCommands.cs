using System.Collections.Immutable;
using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.Application.Devices;

// ===== Port =====================================================

/// <summary>
/// The machine's serial ports, as far as registering a board needs them
/// (ADR 0055 §3).
/// </summary>
public interface ISerialPorts
{
    /// <summary>
    /// The port <paramref name="port"/> names, in a form two names of the
    /// same port share: the file a link such as <c>/dev/serial/by-id/…</c>
    /// resolves to, or a COM port name in one letter case.
    /// </summary>
    string Resolve(string port);

    /// <summary>Whether <paramref name="port"/> is present now.</summary>
    bool Exists(string port);
}

// ===== DTOs =====================================================

/// <summary>Command DTO for <c>visa asrl add</c>.</summary>
/// <param name="Board">The board number.</param>
/// <param name="Port">The port name, as the user wrote it.</param>
public sealed record AddAsrlBoardCommand(int Board, string Port);

/// <summary>The board that was registered.</summary>
/// <param name="Registration">The saved row.</param>
/// <param name="PortMissing">
/// Whether the port was absent when it was registered, which is allowed
/// because a USB adapter appears only while plugged in.
/// </param>
public sealed record AsrlBoardAdded(AsrlBoardRegistration Registration, bool PortMissing);

/// <summary>Command DTO for <c>visa asrl remove</c>.</summary>
/// <param name="Board">The board number.</param>
public sealed record RemoveAsrlBoardCommand(int Board);

/// <summary>Query DTO for <c>visa asrl list</c>.</summary>
public sealed record ListAsrlBoardsQuery;

/// <summary>One registered board and whether its port is present now.</summary>
/// <param name="Registration">The registered row.</param>
/// <param name="PortPresent">Whether the port is present now.</param>
public sealed record AsrlBoardEntry(AsrlBoardRegistration Registration, bool PortPresent);

/// <summary>The registered boards, in registration order.</summary>
/// <param name="Boards">The boards.</param>
public sealed record AsrlBoardListing(ImmutableArray<AsrlBoardEntry> Boards)
{
    /// <summary>Equality over the boards, in order.</summary>
    public bool Equals(AsrlBoardListing? other) =>
        other is not null && Boards.SequenceEqual(other.Boards);

    /// <inheritdoc/>
    public override int GetHashCode() => Boards.Length;
}

// ===== Errors ====================================================

/// <summary>Errors for <c>visa asrl add</c>.</summary>
public abstract record AddAsrlBoardError : IviError
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

/// <summary>
/// The board was negative, or the port was a name an ASRL resource cannot
/// carry: empty, only digits, or containing <c>::</c>.
/// </summary>
public sealed record AddAsrlBoardInvalid(int Board, string Port) : AddAsrlBoardError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid ASRL board {Board} for port {Port}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Board, Port };
}

/// <summary>The board is already registered.</summary>
public sealed record AddAsrlBoardTaken(int Board) : AddAsrlBoardError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "ASRL board {Board} is already registered";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Board };
}

/// <summary>The port, under this or another name, is already registered.</summary>
/// <param name="Port">The port as the user wrote it.</param>
/// <param name="Board">The board it is registered under.</param>
public sealed record AddAsrlPortTaken(string Port, int Board) : AddAsrlBoardError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "port {Port} is already registered as ASRL board {Board}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Port, Board };
}

/// <summary>Loading or saving the configuration failed.</summary>
public sealed record AddAsrlBoardStoreFailure(ConfigStoreError Inner) : AddAsrlBoardError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}

/// <summary>Errors for <c>visa asrl remove</c>.</summary>
public abstract record RemoveAsrlBoardError : IviError
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

/// <summary>The board is not registered.</summary>
public sealed record RemoveAsrlBoardNotFound(int Board) : RemoveAsrlBoardError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "ASRL board {Board} is not registered";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Board };
}

/// <summary>Loading or saving the configuration failed.</summary>
public sealed record RemoveAsrlBoardStoreFailure(ConfigStoreError Inner) : RemoveAsrlBoardError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}

/// <summary>Errors for <c>visa asrl list</c>.</summary>
public abstract record ListAsrlBoardsError : IviError
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

/// <summary>Loading the configuration failed.</summary>
public sealed record ListAsrlBoardsStoreFailure(ConfigStoreError Inner) : ListAsrlBoardsError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}

// ===== Handlers ==================================================

/// <summary>
/// Registers a board. A port is compared with the registered ones by the
/// port it resolves to, so a link and its target are the same port.
/// </summary>
public sealed class AddAsrlBoardCommandHandler
{
    private readonly IConfigStore _store;
    private readonly ISerialPorts _ports;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public AddAsrlBoardCommandHandler(
        IConfigStore store,
        ISerialPorts ports,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _ports = ports;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Validates and saves the registration.</summary>
    public async Task<Result<AsrlBoardAdded, AddAsrlBoardError>> HandleAsync(
        AddAsrlBoardCommand command,
        CancellationToken ct
    )
    {
        if (
            AsrlBoardRegistration.From(command.Board, command.Port)
            is not Result<AsrlBoardRegistration, ConfigError>.Ok { Value: var registration }
        )
        {
            return Fail(new AddAsrlBoardInvalid(command.Board, command.Port));
        }

        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Fail(
                new AddAsrlBoardStoreFailure(
                    ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err
                )
            );
        }

        if (config.FindAsrlBoard(registration.Board) is not null)
        {
            return Fail(new AddAsrlBoardTaken(registration.Board));
        }
        var resolved = _ports.Resolve(registration.Port);
        foreach (var existing in config.AsrlBoards)
        {
            if (_ports.Resolve(existing.Port) == resolved)
            {
                return Fail(new AddAsrlPortTaken(registration.Port, existing.Board));
            }
        }

        var updated = config.AddAsrlBoard(registration) switch
        {
            Result<ConfigDocument, ConfigError>.Ok ok => ok.Value,
            _ => throw new InvalidOperationException(
                "the board and port were checked against the table above"
            ),
        };
        var saveResult = await _store.SaveAsync(updated, ct);
        if (saveResult is Result<Unit, ConfigStoreError>.Error saveErr)
        {
            return Fail(new AddAsrlBoardStoreFailure(saveErr.Err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "asrl.add",
                registration.Board.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _subject.Get()
            ),
            ct
        );
        return Result.Success<AsrlBoardAdded, AddAsrlBoardError>(
            new AsrlBoardAdded(registration, PortMissing: !_ports.Exists(registration.Port))
        );
    }

    private static Result<AsrlBoardAdded, AddAsrlBoardError> Fail(AddAsrlBoardError error) =>
        Result.Failure<AsrlBoardAdded, AddAsrlBoardError>(error);
}

/// <summary>Removes a board registration.</summary>
public sealed class RemoveAsrlBoardCommandHandler
{
    private readonly IConfigStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public RemoveAsrlBoardCommandHandler(
        IConfigStore store,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Removes the board and saves the table.</summary>
    public async Task<Result<Unit, RemoveAsrlBoardError>> HandleAsync(
        RemoveAsrlBoardCommand command,
        CancellationToken ct
    )
    {
        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Fail(
                new RemoveAsrlBoardStoreFailure(
                    ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err
                )
            );
        }

        if (
            config.RemoveAsrlBoard(command.Board)
            is not Result<ConfigDocument, ConfigError>.Ok { Value: var updated }
        )
        {
            return Fail(new RemoveAsrlBoardNotFound(command.Board));
        }
        var saveResult = await _store.SaveAsync(updated, ct);
        if (saveResult is Result<Unit, ConfigStoreError>.Error saveErr)
        {
            return Fail(new RemoveAsrlBoardStoreFailure(saveErr.Err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "asrl.remove",
                command.Board.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _subject.Get()
            ),
            ct
        );
        return Result.Success<Unit, RemoveAsrlBoardError>(Unit.Value);
    }

    private static Result<Unit, RemoveAsrlBoardError> Fail(RemoveAsrlBoardError error) =>
        Result.Failure<Unit, RemoveAsrlBoardError>(error);
}

/// <summary>Lists the registered boards and whether each port is present.</summary>
public sealed class ListAsrlBoardsQueryHandler
{
    private readonly IConfigStore _store;
    private readonly ISerialPorts _ports;

    /// <summary>Creates a new handler.</summary>
    public ListAsrlBoardsQueryHandler(IConfigStore store, ISerialPorts ports)
    {
        _store = store;
        _ports = ports;
    }

    /// <summary>Loads the table.</summary>
    public async Task<Result<AsrlBoardListing, ListAsrlBoardsError>> HandleAsync(
        ListAsrlBoardsQuery query,
        CancellationToken ct
    )
    {
        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Result.Failure<AsrlBoardListing, ListAsrlBoardsError>(
                new ListAsrlBoardsStoreFailure(
                    ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err
                )
            );
        }
        return Result.Success<AsrlBoardListing, ListAsrlBoardsError>(
            new AsrlBoardListing([
                .. config.AsrlBoards.Select(b => new AsrlBoardEntry(b, _ports.Exists(b.Port))),
            ])
        );
    }
}
