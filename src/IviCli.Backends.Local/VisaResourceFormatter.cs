using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>Formats <see cref="VisaResource"/> back to a VISA resource string.</summary>
public static class VisaResourceFormatter
{
    /// <summary>Formats <paramref name="resource"/> to its canonical VISA resource string form.</summary>
    public static string Format(VisaResource resource) =>
        resource switch
        {
            VisaResource.Tcpip t => $"TCPIP{t.Board}::{t.Host}::{t.LanDevice}::INSTR",
            VisaResource.Usb u when u.InterfaceNumber is null =>
                $"USB{u.Board}::{u.VendorId}::{u.ProductId}::{u.SerialNumber}::INSTR",
            VisaResource.Usb u =>
                $"USB{u.Board}::{u.VendorId}::{u.ProductId}::{u.SerialNumber}::{u.InterfaceNumber}::INSTR",
            VisaResource.Gpib g when g.SecondaryAddress is null =>
                $"GPIB{g.Board}::{g.PrimaryAddress}::INSTR",
            VisaResource.Gpib g =>
                $"GPIB{g.Board}::{g.PrimaryAddress}::{g.SecondaryAddress}::INSTR",
            _ => throw new NotSupportedException(
                $"Unsupported VisaResource variant for VISA formatting: {resource.GetType().Name}"
            ),
        };
}
