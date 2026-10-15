using IviCli.Domain.Configuration;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Configuration;

/// <summary>
/// The machine's <c>[[asrl]]</c> table maps a board number to a port
/// (ADR 0055 §3).
/// </summary>
public class AsrlBoardRegistrationTests
{
    private static AsrlBoardRegistration Board(int board, string port) =>
        AsrlBoardRegistration.From(board, port).ShouldBeOk();

    [Theory]
    [InlineData(0, "/dev/ttyUSB0")]
    [InlineData(1, "COM3")]
    public void From_BoardAndPortName_ReturnsTheRegistration(int board, string port)
    {
        // When
        var result = AsrlBoardRegistration.From(board, port);

        // Then
        var registration = result.ShouldBeOk();
        registration.Board.ShouldBe(board);
        registration.Port.ShouldBe(port);
    }

    [Theory]
    [InlineData(-1, "/dev/ttyUSB0")] // negative board
    [InlineData(1, "")] // empty port
    [InlineData(1, "3")] // digits only would name a board
    [InlineData(1, "/dev/tty::USB0")] // the resource separator
    [InlineData(1, " COM7")] // surrounding whitespace would make a second name for COM7
    [InlineData(1, "COM7 ")]
    public void From_Invalid_ReturnsInvalidAsrlBoardRegistration(int board, string port)
    {
        // When
        var result = AsrlBoardRegistration.From(board, port);

        // Then
        var error = result.ShouldBeError().ShouldBeOfType<InvalidAsrlBoardRegistration>();
        error.Board.ShouldBe(board);
        error.Port.ShouldBe(port);
    }

    [Fact]
    public void AddAsrlBoard_NewBoardAndPort_AppendsIt()
    {
        // When
        var result = ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0"));

        // Then
        result.ShouldBeOk().AsrlBoards.ShouldBe([Board(1, "/dev/ttyUSB0")]);
    }

    [Fact]
    public void AddAsrlBoard_BoardAlreadyRegistered_ReturnsDuplicateAsrlBoard()
    {
        // Given
        var config = ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk();

        // When
        var result = config.AddAsrlBoard(Board(1, "/dev/ttyUSB1"));

        // Then
        result.ShouldBeError().ShouldBeOfType<DuplicateAsrlBoard>().Board.ShouldBe(1);
    }

    [Fact]
    public void AddAsrlBoard_PortAlreadyRegistered_ReturnsDuplicateAsrlPort()
    {
        // Given
        var config = ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk();

        // When
        var result = config.AddAsrlBoard(Board(2, "/dev/ttyUSB0"));

        // Then
        var error = result.ShouldBeError().ShouldBeOfType<DuplicateAsrlPort>();
        error.Port.ShouldBe("/dev/ttyUSB0");
        error.Board.ShouldBe(1);
    }

    [Fact]
    public void RemoveAsrlBoard_Registered_RemovesIt()
    {
        // Given
        var config = ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk();

        // When
        var result = config.RemoveAsrlBoard(1);

        // Then
        result.ShouldBeOk().AsrlBoards.ShouldBeEmpty();
    }

    [Fact]
    public void RemoveAsrlBoard_NotRegistered_ReturnsAsrlBoardNotFound()
    {
        // When
        var result = ConfigDocument.Empty.RemoveAsrlBoard(4);

        // Then
        result.ShouldBeError().ShouldBeOfType<AsrlBoardNotFound>().Board.ShouldBe(4);
    }

    [Fact]
    public void FindAsrlBoard_FindsOnlyARegisteredBoard()
    {
        // Given
        var config = ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk();

        // When / Then
        config.FindAsrlBoard(1).ShouldBe(Board(1, "/dev/ttyUSB0"));
        config.FindAsrlBoard(2).ShouldBeNull();
    }

    [Fact]
    public void Equality_CoversTheBoardTable()
    {
        // Given
        var a = ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk();
        var b = ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB1")).ShouldBeOk();

        // When / Then
        a.ShouldNotBe(b);
        a.ShouldBe(ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk());
    }
}
