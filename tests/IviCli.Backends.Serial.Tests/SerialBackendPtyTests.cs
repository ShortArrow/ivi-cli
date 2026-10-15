using System.Diagnostics;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Backends.Serial;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Serial.Tests;

/// <summary>
/// The Serial backend on a real port: one end of a socat pseudo-terminal
/// pair, with the test answering on the other end as the instrument
/// (ADR 0055 §11). Runs on Linux and macOS where socat is installed; the
/// Linux CI job installs it.
/// </summary>
public sealed class SerialBackendPtyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Device Dev(string port, string name = "dmm", int timeoutMs = 500) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse($"ASRL{port}::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    [Requires("socat")]
    public async Task A_query_reaches_the_instrument_and_returns_its_answer()
    {
        await using var pty = await PtyPair.StartAsync(Ct);
        using var instrument = pty.Instrument();
        var device = Dev(pty.Port);
        var backend = new SerialBackend(new SerialPortLineFactory());
        (await backend.OpenAsync(device, Ct)).ShouldBeOk();

        var answer = Task.Run(
            () =>
            {
                instrument.ReadLine().ShouldBe("*IDN?");
                instrument.Write("ACME,DMM,1,2.0\r\n");
            },
            Ct
        );
        var result = await backend.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), Ct);
        await answer;

        result.ShouldBeOk().ShouldBe("ACME,DMM,1,2.0");
        await backend.CloseAsync(device, CancellationToken.None);
    }

    [Requires("socat")]
    public async Task A_read_terminator_of_CR_ends_the_reply_there()
    {
        await using var pty = await PtyPair.StartAsync(Ct);
        using var instrument = pty.Instrument();
        var device = Dev(pty.Port) with { ReadTermination = Termination.Cr };
        var backend = new SerialBackend(new SerialPortLineFactory());
        (await backend.OpenAsync(device, Ct)).ShouldBeOk();

        instrument.Write("1.5\r");
        var result = await backend.ReadAsync(device, Ct);

        result.ShouldBeOk().ShouldBe("1.5");
        await backend.CloseAsync(device, CancellationToken.None);
    }

    [Requires("socat")]
    public async Task A_silent_instrument_times_out_after_the_device_timeout()
    {
        await using var pty = await PtyPair.StartAsync(Ct);
        using var instrument = pty.Instrument();
        var device = Dev(pty.Port, timeoutMs: 300);
        var backend = new SerialBackend(new SerialPortLineFactory());
        (await backend.OpenAsync(device, Ct)).ShouldBeOk();

        var watch = Stopwatch.StartNew();
        var result = await backend.ReadAsync(device, Ct);
        watch.Stop();

        result.ShouldBeError().ShouldBeOfType<TransportTimeout>();
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        await backend.CloseAsync(device, CancellationToken.None);
    }

    [Requires("socat")]
    public async Task A_closed_port_opens_again()
    {
        await using var pty = await PtyPair.StartAsync(Ct);
        using var instrument = pty.Instrument();
        var device = Dev(pty.Port);
        var backend = new SerialBackend(new SerialPortLineFactory());

        (await backend.OpenAsync(device, Ct)).ShouldBeOk();
        (await backend.CloseAsync(device, Ct)).ShouldBeOk();
        (await backend.OpenAsync(device, Ct)).ShouldBeOk();
        (await backend.WriteAsync(device, ScpiCommand.From("*RST").ShouldBeOk(), Ct)).ShouldBeOk();

        instrument.ReadLine().ShouldBe("*RST");
        await backend.CloseAsync(device, CancellationToken.None);
    }

    /// <summary>
    /// A second session on a port one already holds fails as in use
    /// (ADR 0055 §7): ivi-cli opens a port for exclusive use.
    /// </summary>
    [Requires("socat")]
    public async Task A_port_another_session_holds_is_in_use()
    {
        await using var pty = await PtyPair.StartAsync(Ct);
        var first = Dev(pty.Port, name: "first");
        var second = Dev(pty.Port, name: "second");
        var backend = new SerialBackend(new SerialPortLineFactory());
        (await backend.OpenAsync(first, Ct)).ShouldBeOk();

        var result = await backend.OpenAsync(second, Ct);

        result.ShouldBeError().ShouldBeOfType<TransportInUse>();
        await backend.CloseAsync(first, CancellationToken.None);
    }

    /// <summary>
    /// A socat pseudo-terminal pair: <see cref="Port"/> for the backend and
    /// the other end for the test to answer on as the instrument.
    /// </summary>
    private sealed class PtyPair : IAsyncDisposable
    {
        private readonly Process _socat;
        private readonly string _directory;

        private PtyPair(Process socat, string directory)
        {
            _socat = socat;
            _directory = directory;
        }

        public string Port => Path.Combine(_directory, "port");

        private string InstrumentEnd => Path.Combine(_directory, "instrument");

        public static async Task<PtyPair> StartAsync(CancellationToken ct)
        {
            var directory = Directory.CreateTempSubdirectory("ivicli-pty-").FullName;
            var psi = new ProcessStartInfo("socat")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add($"pty,raw,echo=0,link={Path.Combine(directory, "port")}");
            psi.ArgumentList.Add($"pty,raw,echo=0,link={Path.Combine(directory, "instrument")}");
            var socat =
                Process.Start(psi) ?? throw new InvalidOperationException("socat did not start");
            var pair = new PtyPair(socat, directory);
            for (
                var i = 0;
                i < 100 && !(File.Exists(pair.Port) && File.Exists(pair.InstrumentEnd));
                i++
            )
            {
                await Task.Delay(50, ct);
            }
            File.Exists(pair.Port).ShouldBeTrue("socat made no pseudo-terminal");
            return pair;
        }

        /// <summary>The instrument's end, read and written a line at a time.</summary>
        public InstrumentEnd Instrument() => new(InstrumentEnd);

        public async ValueTask DisposeAsync()
        {
            if (!_socat.HasExited)
            {
                _socat.Kill();
                await _socat.WaitForExitAsync(CancellationToken.None);
            }
            _socat.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>The instrument's side of the pair.</summary>
    private sealed class InstrumentEnd(string path) : IDisposable
    {
        private readonly FileStream _stream = new(path, FileMode.Open, FileAccess.ReadWrite);

        /// <summary>Reads one LF-terminated line, without the LF.</summary>
        public string ReadLine()
        {
            var line = new StringBuilder();
            while (true)
            {
                var b = _stream.ReadByte();
                if (b is -1 or '\n')
                {
                    return line.ToString();
                }
                line.Append((char)b);
            }
        }

        public void Write(string text)
        {
            _stream.Write(Encoding.Latin1.GetBytes(text));
            _stream.Flush();
        }

        public void Dispose() => _stream.Dispose();
    }
}
