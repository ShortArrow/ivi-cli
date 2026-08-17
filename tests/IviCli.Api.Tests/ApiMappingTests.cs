using IviCli.Api.Mapping;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests;

/// <summary>
/// The device DTO carries the resource string the operator registered,
/// port suffix included — an API client that reads it back must be able
/// to dial the same endpoint.
/// </summary>
public sealed class ApiMappingTests
{
    [Theory]
    [InlineData("TCPIP0::192.168.0.10::hislip0,5000::INSTR")]
    [InlineData("TCPIP0::192.168.0.10::gpib0,5::INSTR")]
    [InlineData("USB0::0x0699::0x0408::C012345::1::INSTR")]
    public void A_device_dto_carries_the_canonical_resource_string(string resource)
    {
        var device = new Device(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

        device.ToDto().Resource.ShouldBe(resource);
    }
}
