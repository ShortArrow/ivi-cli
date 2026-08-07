# Verified Instruments

Real-instrument compatibility is a point-in-time record: an entry states
what was observed at the recorded ivi-cli version and is not re-verified
on later releases. Instruments not listed fall under best-effort
standards conformance (HiSLIP, VXI-11, raw SOCKET, IEEE 488.2 / SCPI) —
incompatibility with a spec-conforming instrument is a bug; attach an
`IVICLI_CAPTURE` traffic log to the report.

| Instrument | Verified at | Result |
| --- | --- | --- |
| Kikusui PWR801L | v0.2.6 | SOCKET (5025) and HiSLIP (4880) round-trips work; LAN discovery finds it. VXI-11 portmapper resolution works, but queries are blocked by a device-side Core-port issue. |
