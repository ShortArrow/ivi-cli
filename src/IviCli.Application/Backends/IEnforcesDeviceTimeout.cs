namespace IviCli.Application.Backends;

/// <summary>
/// Marks an <see cref="IIviBackend"/> that bounds each operation by the
/// device's timeout on its own: VXI-11, whose server enforces the
/// <c>io_timeout</c> it is sent, and a VISA runtime, which enforces
/// <c>VI_ATTR_TMO_VALUE</c>. <see cref="DeviceTimeoutBackendFactory"/>
/// gives such a backend a grace period beyond the device timeout, so the
/// backend's own timeout error arrives first.
/// </summary>
public interface IEnforcesDeviceTimeout;
