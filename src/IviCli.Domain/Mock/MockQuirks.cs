namespace IviCli.Domain.Mock;

/// <summary>
/// Firmware misbehaviour a scenario asks the mock to reproduce, so that
/// code consuming a backend's service-request stream can be tested
/// against the shapes real instruments actually take (issue #115). Each
/// quirk is one optional field; a scenario that names none carries no
/// quirks at all and the mock behaves ideally.
/// </summary>
/// <param name="SrqNotifyWedgeAfter">
/// Number of service requests the mock delivers to its
/// <c>ServiceRequestStream</c> before the notification path wedges:
/// later raises still update the status byte, but no notification ever
/// arrives again until the device is reopened. Models a Kikusui PWR401L
/// whose USB488 notification machinery stops after certain session
/// histories while serial poll keeps reporting MSS (recorded on
/// PR #114). <c>0</c> wedges the stream before the first delivery.
/// </param>
public sealed record MockQuirks(int? SrqNotifyWedgeAfter = null)
{
    /// <summary>
    /// True when no quirk is named. An empty profile is indistinguishable
    /// from no profile, so parsers and serialisers can treat both as the
    /// absence of the <c>[quirks]</c> table.
    /// </summary>
    public bool IsEmpty => SrqNotifyWedgeAfter is null;
}
