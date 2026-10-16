using Ivi.Visa;
using IviCli.Backends.Local;
using IviCli.Domain.Devices;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// A session VISA has just opened is set up from the device: its I/O
/// timeout, its serial line and its read terminator (ADR 0055 §4, §5); one
/// that fails to set up is disposed rather than left to the finalizer.
/// </summary>
public sealed class VisaSessionSetUpTests
{
    private static readonly TimeSpan Io = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public void A_session_without_serial_settings_or_terminator_gets_only_its_timeout()
    {
        var (session, recorder) = RecordingSerialSession.Create();

        VisaSessionFactory.SetUp(session, new VisaSessionOptions(Io, Io)).ShouldBeOk();

        recorder.Sets.ShouldBe([("TimeoutMilliseconds", (object)1500)]);
    }

    [Fact]
    public void A_serial_session_gets_its_serial_settings()
    {
        var settings = SerialSettings
            .From(
                19200,
                7,
                Domain.Devices.SerialParity.Even,
                SerialStopBits.Two,
                SerialFlowControl.RtsCts
            )
            .ShouldBeOk();
        var (session, recorder) = RecordingSerialSession.Create();

        VisaSessionFactory.SetUp(session, new VisaSessionOptions(Io, Io, settings)).ShouldBeOk();

        recorder.Sets.ShouldContain(("BaudRate", (object)19200));
        recorder.Sets.ShouldContain(("DataTerminalReadyState", (object)LineState.Asserted));
        recorder.Disposed.ShouldBeFalse();
    }

    [Fact]
    public void A_read_terminator_sets_and_enables_its_last_character()
    {
        var (session, recorder) = RecordingSerialSession.Create();

        VisaSessionFactory
            .SetUp(session, new VisaSessionOptions(Io, Io, ReadTermination: Termination.CrLf))
            .ShouldBeOk();

        recorder.Sets.ShouldContain(("TerminationCharacter", (object)(byte)'\n'));
        recorder.Sets.ShouldContain(("TerminationCharacterEnabled", (object)true));
    }

    [Fact]
    public void A_session_whose_serial_setting_is_refused_is_disposed()
    {
        var (session, recorder) = RecordingSerialSession.Create();
        recorder.Refuses = "StopBits";

        VisaSessionFactory
            .SetUp(session, new VisaSessionOptions(Io, Io, SerialSettings.Default))
            .ShouldBeError()
            .ShouldBeOfType<LocalVisaSettingRefused>()
            .Setting.ShouldBe("stop_bits");

        recorder.Disposed.ShouldBeTrue();
    }

    [Fact]
    public void A_session_that_fails_to_set_up_is_disposed()
    {
        var (session, recorder) = RecordingSerialSession.Create();
        recorder.Fails = "TerminationCharacter";

        Should.Throw<InvalidOperationException>(() =>
            VisaSessionFactory.SetUp(
                session,
                new VisaSessionOptions(Io, Io, ReadTermination: Termination.Lf)
            )
        );

        recorder.Disposed.ShouldBeTrue();
    }
}
