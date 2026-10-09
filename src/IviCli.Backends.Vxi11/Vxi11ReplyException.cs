namespace IviCli.Backends.Vxi11;

/// <summary>
/// A VXI-11 call that the server answered with a non-zero error code.
/// </summary>
/// <param name="procedure">The RPC procedure, such as <c>device_write</c>.</param>
/// <param name="code">The VXI-11 error code (Rev 1.0, B.5.3); 15 is an I/O timeout.</param>
internal sealed class Vxi11ReplyException(string procedure, int code)
    : IOException($"{procedure} returned error {code}")
{
    /// <summary>The VXI-11 error code the server returned.</summary>
    public int Code { get; } = code;
}
