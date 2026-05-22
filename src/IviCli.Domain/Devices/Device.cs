using IviCli.Domain.Visa;

namespace IviCli.Domain.Devices;

/// <summary>
/// A configured instrument, identified by an alias <see cref="DeviceName"/>
/// and addressed via a <see cref="VisaResource"/> with a per-device
/// <see cref="Domain.Timeout"/>.
/// </summary>
/// <remarks>
/// <para>
/// All component fields are Value Objects with their own validation, so the
/// record itself trusts its inputs. Higher-level construction (parsing config
/// or CLI arguments) lives in the Application layer.
/// </para>
/// <para>
/// Equality is structural by default. The uniqueness invariant on
/// <see cref="DeviceName"/> within a configuration is enforced at the
/// <c>ConfigDocument</c> level, not on this record.
/// </para>
/// </remarks>
/// <param name="Name">The unique alias.</param>
/// <param name="Resource">The VISA resource that addresses the instrument.</param>
/// <param name="Timeout">The per-device default operation timeout.</param>
public sealed record Device(DeviceName Name, VisaResource Resource, Timeout Timeout);
