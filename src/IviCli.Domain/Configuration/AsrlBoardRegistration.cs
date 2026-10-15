namespace IviCli.Domain.Configuration;

/// <summary>
/// One row of the machine's <c>[[asrl]]</c> table: the port that
/// <c>ASRL&lt;board&gt;::INSTR</c> means on this machine (ADR 0055 §3).
/// </summary>
public sealed record AsrlBoardRegistration
{
    /// <summary>The board number; never negative.</summary>
    public int Board { get; }

    /// <summary>
    /// The port name, such as <c>/dev/ttyUSB0</c> or <c>COM3</c>; never empty,
    /// never only digits and never containing <c>::</c>, so that
    /// <c>ASRL&lt;port&gt;::INSTR</c> names it.
    /// </summary>
    public string Port { get; }

    private AsrlBoardRegistration(int board, string port)
    {
        Board = board;
        Port = port;
    }

    /// <summary>Validates and constructs a registration.</summary>
    /// <returns>The registration, or <see cref="InvalidAsrlBoardRegistration"/>.</returns>
    public static Result<AsrlBoardRegistration, ConfigError> From(int board, string port) =>
        board < 0 || port.Length == 0 || port.All(char.IsAsciiDigit) || port.Contains("::")
            ? Result.Failure<AsrlBoardRegistration, ConfigError>(
                new InvalidAsrlBoardRegistration(board, port)
            )
            : Result.Success<AsrlBoardRegistration, ConfigError>(
                new AsrlBoardRegistration(board, port)
            );
}
