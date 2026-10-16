using System.Collections.Immutable;
using IviCli.Application.Devices;
using IviCli.Cli.Commands;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// <c>visa list</c> shows a serial device's line and every device's
/// terminators, named as in <c>config.toml</c> (ADR 0055 §10).
/// </summary>
public sealed class DeviceListingFormatterTests
{
    private static Device Dev(
        string name,
        string resource,
        int timeoutMs,
        SerialSettings? serial = null,
        Termination? read = null,
        Termination? write = null
    ) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk(),
            serial,
            read,
            write
        );

    /// <summary>
    /// A board device with VISA's defaults and both terminators, a port
    /// whose name needs escaping in JSON with its own line, and a network
    /// device with neither.
    /// </summary>
    private static DeviceListing SerialListing() =>
        new(
            [
                Dev("dmm", "ASRL1::INSTR", 2000, read: Termination.Cr, write: Termination.CrLf),
                Dev(
                    "meter",
                    @"ASRL\\.\COM10::INSTR",
                    2000,
                    SerialSettings
                        .From(
                            19200,
                            7,
                            SerialParity.Even,
                            SerialStopBits.OnePointFive,
                            SerialFlowControl.RtsCts
                        )
                        .ShouldBeOk()
                ),
                Dev("psu1", "TCPIP0::192.168.0.10::inst0::INSTR", 3000),
            ],
            DefaultDevice: DeviceName.From("dmm").ShouldBeOk()
        );

    [Fact]
    public void FormatJson_adds_the_serial_line_and_the_terminators_that_are_set()
    {
        DeviceListingFormatter
            .FormatJson(SerialListing())
            .ShouldBe(
                """
                {"devices":[{"name":"dmm","resource":"ASRL1::INSTR","timeout_ms":2000,"serial":{"baud":9600,"data_bits":8,"parity":"none","stop_bits":1,"flow_control":"none"},"read_termination":"\r","write_termination":"\r\n"},{"name":"meter","resource":"ASRL\\\\.\\COM10::INSTR","timeout_ms":2000,"serial":{"baud":19200,"data_bits":7,"parity":"even","stop_bits":1.5,"flow_control":"rts_cts"}},{"name":"psu1","resource":"TCPIP0::192.168.0.10::inst0::INSTR","timeout_ms":3000}],"default":"dmm"}

                """
            );
    }

    [Fact]
    public void FormatHuman_adds_the_serial_line_and_the_terminators_that_are_set()
    {
        DeviceListingFormatter
            .FormatHuman(SerialListing())
            .ShouldBe(
                "* dmm\tASRL1::INSTR\t2000ms\t9600 8-N-1\tread=\\r write=\\r\\n\n"
                    + "  meter\tASRL\\\\.\\COM10::INSTR\t2000ms\t19200 7-E-1.5 rts_cts\n"
                    + "  psu1\tTCPIP0::192.168.0.10::inst0::INSTR\t3000ms\n"
            );
    }

    [Fact]
    public void FormatHuman_shows_a_terminator_on_a_device_that_is_not_serial()
    {
        var listing = new DeviceListing(
            [Dev("sock", "TCPIP0::host::5025::SOCKET", 3000, write: Termination.Lf)],
            DefaultDevice: null
        );

        DeviceListingFormatter
            .FormatHuman(listing)
            .ShouldBe("  sock\tTCPIP0::host::5025::SOCKET\t3000ms\twrite=\\n\n");
    }

    [Fact]
    public void FormatJson_writes_text_other_than_ASCII_and_symbols_as_they_are()
    {
        var listing = new DeviceListing(
            [
                Dev("a", "TCPIP0::keisoku-é.local::inst0::INSTR", 3000),
                Dev("b", "USB0::0x1234::0x5678::A+B&C'D::INSTR", 3000),
            ],
            DefaultDevice: null
        );

        DeviceListingFormatter
            .FormatJson(listing)
            .ShouldBe(
                """
                {"devices":[{"name":"a","resource":"TCPIP0::keisoku-é.local::inst0::INSTR","timeout_ms":3000},{"name":"b","resource":"USB0::0x1234::0x5678::A+B&C'D::INSTR","timeout_ms":3000}],"default":null}

                """
            );
    }
}
