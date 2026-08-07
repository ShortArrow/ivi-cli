namespace IviCli.Domain.Protocols;

/// <summary>
/// The service-request half of the USB488 subclass (ADR 0049 §2): the
/// status byte an instrument keeps, the SRQ condition that byte can
/// raise, and the two-byte notifications the interrupt-IN endpoint
/// carries to the host (USB488 1.00 §3.4.1).
///
/// The model is the serial poll of IEEE 488.2 §11.2. A backend event
/// asserts the request; the host learns of it from a notification, reads
/// the status byte with READ_STATUS_BYTE, and that read clears RQS and
/// ends the condition. While a condition stands, a further backend event
/// updates the status byte the next poll will report but raises no second
/// notification — the request line of a real instrument is already
/// asserted and asserting it again is not an edge. That single place is
/// where a quirk profile whose notifications wedge until a power cycle
/// (issue #115) will diverge from this well-behaved device.
///
/// Deliberately free of transport: what turns a queued notification into
/// a completed interrupt-IN URB is the USB/IP server above.
/// </summary>
public sealed class Usb488Notifier
{
    private readonly Queue<byte[]> _notifications = new();
    private byte _statusByte;
    private bool _serviceRequestPending;

    /// <summary>
    /// The status byte the next serial poll will report — the value the
    /// backend last raised, less the RQS bit of any request already
    /// polled away.
    /// </summary>
    public byte StatusByte => _statusByte;

    /// <summary>
    /// Whether <paramref name="bTag"/> is one a READ_STATUS_BYTE may
    /// name. The answer's <c>bNotify1</c> is <c>0x80 | bTag</c>, and a
    /// host tells a serial-poll answer from an SRQ by that byte being
    /// above <see cref="UsbTmcConstants.NotifyServiceRequest"/> — so tags
    /// below <see cref="UsbTmcConstants.MinimumStatusByteBTag"/> would
    /// arrive as service requests, and a tag that does not fit seven bits
    /// would collide with the notification flag.
    /// </summary>
    public static bool IsReadableStatusByteTag(ushort bTag) =>
        bTag
            is >= UsbTmcConstants.MinimumStatusByteBTag
                and <= UsbTmcConstants.MaximumStatusByteBTag;

    /// <summary>
    /// Records a service request the backend raised, with the status byte
    /// it carries. Queues the SRQ notification of USB488 1.00 §3.4.1 for
    /// the interrupt-IN endpoint unless a request is already outstanding.
    /// </summary>
    public void RaiseServiceRequest(byte statusByte)
    {
        _statusByte = statusByte;

        if (_serviceRequestPending)
        {
            return;
        }

        _serviceRequestPending = true;
        _notifications.Enqueue([UsbTmcConstants.NotifyServiceRequest, statusByte]);
    }

    /// <summary>
    /// Serves one READ_STATUS_BYTE (USB488 1.00 §4.3.1) and returns the
    /// three bytes its control transfer answers with:
    /// <c>USBTMC_STATUS_SUCCESS</c>, the <c>bTag</c> echoed, and the
    /// status byte.
    ///
    /// A host that claimed the interrupt endpoint takes the status from
    /// the notification this call also queues and ignores the third byte
    /// — that is what the Linux driver does. The byte is filled in
    /// regardless, because a host that never claimed the endpoint has
    /// nowhere else to read it from.
    ///
    /// The read is what ends a service request: RQS is cleared here, and
    /// the next backend event is a new condition with its own
    /// notification.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bTag"/> is not one
    /// <see cref="IsReadableStatusByteTag"/> accepts.
    /// </exception>
    public byte[] ReadStatusByte(byte bTag)
    {
        if (!IsReadableStatusByteTag(bTag))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bTag),
                bTag,
                $"USB488 READ_STATUS_BYTE takes a bTag of {UsbTmcConstants.MinimumStatusByteBTag}"
                    + $"..{UsbTmcConstants.MaximumStatusByteBTag}"
            );
        }

        var reported = _statusByte;
        _notifications.Enqueue([(byte)(UsbTmcConstants.NotifyFlag | bTag), reported]);
        _statusByte = (byte)(reported & ~UsbTmcConstants.StatusByteRequestService);
        _serviceRequestPending = false;

        return [UsbTmcConstants.StatusSuccess, bTag, reported];
    }

    /// <summary>
    /// Takes the oldest notification waiting for the interrupt-IN
    /// endpoint. False when none is waiting, which is the ordinary state
    /// of an instrument with nothing to report.
    /// </summary>
    public bool TryTakeNotification(out byte[] packet)
    {
        if (_notifications.Count == 0)
        {
            packet = [];
            return false;
        }

        packet = _notifications.Dequeue();
        return true;
    }
}
