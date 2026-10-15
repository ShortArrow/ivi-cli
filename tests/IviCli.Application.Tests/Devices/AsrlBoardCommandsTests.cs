using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

/// <summary>
/// <c>visa asrl add</c>, <c>list</c> and <c>remove</c> manage the machine's
/// board table (ADR 0055 §3).
/// </summary>
public sealed class AsrlBoardCommandsTests
{
    private static AsrlBoardRegistration Board(int board, string port) =>
        AsrlBoardRegistration.From(board, port).ShouldBeOk();

    private static async Task<ConfigDocument> Saved(FakeConfigStore store) =>
        (await store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBeOk();

    [Fact]
    public async Task Adding_a_board_for_a_present_port_saves_it()
    {
        // Given
        var store = new FakeConfigStore();
        var ports = new FakeSerialPorts().Present("/dev/ttyUSB0");
        var handler = new AddAsrlBoardCommandHandler(store, ports);

        // When
        var result = await handler.HandleAsync(
            new AddAsrlBoardCommand(1, "/dev/ttyUSB0"),
            TestContext.Current.CancellationToken
        );

        // Then
        var added = result.ShouldBeOk();
        added.Registration.ShouldBe(Board(1, "/dev/ttyUSB0"));
        added.PortMissing.ShouldBeFalse();
        (await Saved(store)).AsrlBoards.ShouldBe([Board(1, "/dev/ttyUSB0")]);
    }

    [Fact]
    public async Task Adding_a_board_for_a_missing_port_saves_it_and_says_so()
    {
        // Given
        var store = new FakeConfigStore();
        var handler = new AddAsrlBoardCommandHandler(store, new FakeSerialPorts());

        // When
        var result = await handler.HandleAsync(
            new AddAsrlBoardCommand(1, "/dev/ttyUSB0"),
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeOk().PortMissing.ShouldBeTrue();
        (await Saved(store)).AsrlBoards.ShouldBe([Board(1, "/dev/ttyUSB0")]);
    }

    [Fact]
    public async Task Adding_a_board_that_is_registered_fails()
    {
        // Given
        var store = new FakeConfigStore(
            ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk()
        );
        var handler = new AddAsrlBoardCommandHandler(store, new FakeSerialPorts());

        // When
        var result = await handler.HandleAsync(
            new AddAsrlBoardCommand(1, "/dev/ttyUSB1"),
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<AddAsrlBoardTaken>().Board.ShouldBe(1);
    }

    /// <summary>
    /// A link such as <c>/dev/serial/by-id/…</c> is the port it points at,
    /// so registering it under a second board fails.
    /// </summary>
    [Fact]
    public async Task Adding_another_name_for_a_registered_port_fails()
    {
        // Given
        var store = new FakeConfigStore(
            ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk()
        );
        var ports = new FakeSerialPorts().Link("/dev/serial/by-id/usb-FTDI", "/dev/ttyUSB0");
        var handler = new AddAsrlBoardCommandHandler(store, ports);

        // When
        var result = await handler.HandleAsync(
            new AddAsrlBoardCommand(2, "/dev/serial/by-id/usb-FTDI"),
            TestContext.Current.CancellationToken
        );

        // Then
        var taken = result.ShouldBeError().ShouldBeOfType<AddAsrlPortTaken>();
        taken.Port.ShouldBe("/dev/serial/by-id/usb-FTDI");
        taken.Board.ShouldBe(1);
        (await Saved(store)).AsrlBoards.ShouldBe([Board(1, "/dev/ttyUSB0")]);
    }

    [Theory]
    [InlineData(-1, "/dev/ttyUSB0")]
    [InlineData(1, "3")]
    public async Task Adding_an_invalid_board_fails(int board, string port)
    {
        // Given
        var handler = new AddAsrlBoardCommandHandler(new FakeConfigStore(), new FakeSerialPorts());

        // When
        var result = await handler.HandleAsync(
            new AddAsrlBoardCommand(board, port),
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<AddAsrlBoardInvalid>();
    }

    [Fact]
    public async Task Removing_a_registered_board_saves_the_table_without_it()
    {
        // Given
        var store = new FakeConfigStore(
            ConfigDocument.Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0")).ShouldBeOk()
        );
        var handler = new RemoveAsrlBoardCommandHandler(store);

        // When
        var result = await handler.HandleAsync(
            new RemoveAsrlBoardCommand(1),
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeOk();
        (await Saved(store)).AsrlBoards.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_an_unregistered_board_fails()
    {
        // Given
        var handler = new RemoveAsrlBoardCommandHandler(new FakeConfigStore());

        // When
        var result = await handler.HandleAsync(
            new RemoveAsrlBoardCommand(3),
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<RemoveAsrlBoardNotFound>().Board.ShouldBe(3);
    }

    [Fact]
    public async Task Listing_returns_each_board_and_whether_its_port_is_present()
    {
        // Given
        var store = new FakeConfigStore(
            ConfigDocument
                .Empty.AddAsrlBoard(Board(1, "/dev/ttyUSB0"))
                .ShouldBeOk()
                .AddAsrlBoard(Board(2, "COM3"))
                .ShouldBeOk()
        );
        var handler = new ListAsrlBoardsQueryHandler(
            store,
            new FakeSerialPorts().Present("/dev/ttyUSB0")
        );

        // When
        var result = await handler.HandleAsync(
            new ListAsrlBoardsQuery(),
            TestContext.Current.CancellationToken
        );

        // Then
        result
            .ShouldBeOk()
            .Boards.ShouldBe([
                new AsrlBoardEntry(Board(1, "/dev/ttyUSB0"), PortPresent: true),
                new AsrlBoardEntry(Board(2, "COM3"), PortPresent: false),
            ]);
    }

    /// <summary>
    /// Ports answered from tables: <see cref="Link"/> names another name for
    /// a port, and <see cref="Present"/> marks a port as plugged in.
    /// </summary>
    private sealed class FakeSerialPorts : ISerialPorts
    {
        private readonly Dictionary<string, string> _links = [];
        private readonly HashSet<string> _present = [];

        public FakeSerialPorts Link(string name, string target)
        {
            _links[name] = target;
            return this;
        }

        public FakeSerialPorts Present(string port)
        {
            _present.Add(port);
            return this;
        }

        public string Resolve(string port) => _links.GetValueOrDefault(port, port);

        public bool Exists(string port) => _present.Contains(Resolve(port));
    }
}
