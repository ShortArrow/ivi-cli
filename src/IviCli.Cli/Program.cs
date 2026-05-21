// Composition root for the ivi-cli command-line entry point.
// Real wiring (System.CommandLine, DI registration, Serilog setup) is introduced
// incrementally via TDD per the project's ADRs.

namespace IviCli.Cli;

internal static class Program
{
    public static int Main(string[] args) => 0;
}
