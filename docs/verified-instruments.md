# Verified Instruments

Real-instrument compatibility is a point-in-time record: an entry states
what was observed at the recorded ivi-cli version and is not re-verified
on later releases. Instruments not listed fall under best-effort
standards conformance (HiSLIP, VXI-11, raw SOCKET, IEEE 488.2 / SCPI) —
incompatibility with a spec-conforming instrument is a bug; attach an
`IVICLI_CAPTURE` traffic log to the report.

| Instrument | Verified at | SOCKET | HiSLIP | VXI-11 | USB | GPIB | Notes |
| --- | --- | :-: | :-: | :-: | :-: | :-: | --- |
| Kikusui PWR801L | v0.2.6 | ✅ | ✅ | ⚠ | — | — | LAN discovery finds it. VXI-11: portmapper resolution works, but queries are blocked by a device-side Core-port issue. |
| Kikusui PWR401L | v0.3.0 | — | — | — | ⚠ | — | Enumerated and queried through NI-VISA on Windows (`visa scan`, `*IDN?`, IEEE 488.2 status sequence). SRQ: after some session histories the instrument keeps asserting MSS in `*STB?` but sends no USB488 notification until a power cycle, so no host-side event arrives; the mock's `srq_notify_wedge_after` quirk reproduces the shape. |

✅ round-trip verified · ⚠ partial, see notes · — not verified at that version
