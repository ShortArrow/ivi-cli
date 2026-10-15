using System.Collections.Immutable;
using System.Text.Json;
using IviCli.Application.Devices;
using IviCli.Cli.Commands;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>What <c>visa asrl list</c> prints, as text and as JSON.</summary>
public sealed class VisaAsrlCommandRenderTests
{
    private static AsrlBoardEntry Entry(int board, string port, bool present) =>
        new(AsrlBoardRegistration.From(board, port).ShouldBeOk(), present);

    private static AsrlBoardListing Listing(params AsrlBoardEntry[] entries) => new([.. entries]);

    [Fact]
    public void An_empty_listing_says_so()
    {
        var writer = new StringWriter();

        VisaAsrlCommand.Render(Listing(), writer);

        writer.ToString().ShouldBe("(no ASRL boards registered)" + Environment.NewLine);
    }

    [Fact]
    public void Each_board_shows_its_resource_and_port_and_an_absent_port_is_marked()
    {
        var writer = new StringWriter();

        VisaAsrlCommand.Render(
            Listing(Entry(1, "/dev/ttyUSB0", present: true), Entry(2, "COM3", present: false)),
            writer
        );

        writer
            .ToString()
            .ShouldBe(
                "ASRL1::INSTR -> /dev/ttyUSB0"
                    + Environment.NewLine
                    + "ASRL2::INSTR -> COM3 (not present)"
                    + Environment.NewLine
            );
    }

    [Fact]
    public void The_json_lists_board_port_and_presence_and_escapes_the_port()
    {
        var json = VisaAsrlCommand.RenderJson(Listing(Entry(1, @"\\.\COM10", present: false)));

        using var document = JsonDocument.Parse(json);
        var board = document.RootElement.GetProperty("boards")[0];
        board.GetProperty("board").GetInt32().ShouldBe(1);
        board.GetProperty("port").GetString().ShouldBe(@"\\.\COM10");
        board.GetProperty("present").GetBoolean().ShouldBeFalse();
    }
}
