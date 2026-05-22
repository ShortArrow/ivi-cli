using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests;

/// <summary>
/// Smoke tests asserting that every Domain error variant implements the
/// <see cref="IviError"/> contract described in ADR 0014 §9.
/// </summary>
public class IviErrorContractTests
{
    public static TheoryData<IviError> AllDomainErrors() =>
        new()
        {
            new InvalidDeviceNameFormat("1bad"),
            new InvalidVisaResourceFormat("not a resource"),
            new InvalidTimeoutValue(TimeSpan.FromHours(2)),
            new DuplicateDeviceName(DeviceName.From("psu1").ShouldBeOk()),
            new DeviceNotFound(DeviceName.From("ghost").ShouldBeOk()),
            new DefaultDeviceMissing(DeviceName.From("ghost").ShouldBeOk()),
        };

    [Theory]
    [MemberData(nameof(AllDomainErrors))]
    public void DomainError_ExposesSeverityMessageAndArgs(IviError error)
    {
        // Then
        error.Message.ShouldNotBeNullOrEmpty();
        error.LogArgs.ShouldNotBeNull();
        // Severity is a value type; presence of the property is sufficient
        // here — values are per-variant business decisions.
        _ = error.Severity;
    }
}
