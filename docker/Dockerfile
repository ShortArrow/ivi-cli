# syntax=docker/dockerfile:1.7
#
# Mock VISA container image for ivi-cli (ADR 0018).
#
# Built from the self-contained linux-x64 / linux-arm64 publish output
# produced by the release.yml `publish` job (or local `dotnet publish`).
# The build context is the repository root; pass the publish output via
# the RID build-arg so the correct artifact directory is selected.
#
# Local build (after `dotnet publish ... -o ./artifacts/linux-x64-selfcontained`):
#   docker build -f docker/Dockerfile --build-arg RID=linux-x64 -t ivi-cli-mock:dev .
#
# Run:
#   docker run --rm -p 4880:4880 -p 5025:5025 ivi-cli-mock:dev

# RID selects the self-contained publish directory under
# ./artifacts/<RID>-selfcontained/. For multi-arch buildx invocations
# we honor the standard $TARGETARCH (amd64 / arm64) and remap to the
# matching .NET RID; single-arch local builds can override directly
# via `--build-arg RID=linux-x64`.
ARG TARGETARCH=amd64
ARG RID=
ARG RUNTIME_DEPS_TAG=9.0-bookworm-slim

# Microsoft `runtime-deps` ships the exact native dependencies a
# self-contained .NET binary needs (libc, libssl, libgcc, libstdc++,
# tzdata, ca-certificates) with no .NET runtime layer — the runtime
# lives inside our self-contained publish output. Debian (glibc) is
# the natural match for the `linux-x64` / `linux-arm64` RIDs the
# release.yml publish job emits today.
#
# We pin to the 9.0 base tag rather than 10.0 because the self-
# contained binary bundles its own .NET 10 runtime; the base image
# only needs to provide the OS-level native deps (libc, libssl,
# tzdata, ca-certificates), which are stable across .NET versions.
# Switch to 10.0-bookworm-slim once Microsoft publishes that tag.
FROM mcr.microsoft.com/dotnet/runtime-deps:${RUNTIME_DEPS_TAG}

ARG TARGETARCH
ARG RID

# Resolve RID from TARGETARCH when not supplied explicitly.
# Buildx sets TARGETARCH=amd64 / arm64; .NET RIDs are linux-x64 / linux-arm64.
RUN RID_RESOLVED="${RID}"; \
    if [ -z "$RID_RESOLVED" ]; then \
        case "${TARGETARCH}" in \
            amd64) RID_RESOLVED="linux-x64" ;; \
            arm64) RID_RESOLVED="linux-arm64" ;; \
            *) echo "unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
        esac; \
    fi; \
    echo "${RID_RESOLVED}" > /tmp/RID

# `netcat-openbsd` provides the `nc` HEALTHCHECK probe; `bash` is the
# entrypoint shell. `--no-install-recommends` keeps the layer minimal.
RUN apt-get update \
    && apt-get install -y --no-install-recommends bash netcat-openbsd \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/ivi-cli

# COPY cannot expand $TARGETARCH directly, so we stage all per-RID
# publish outputs and pick the right one at runtime of the build step.
# Both amd64 and arm64 publish directories must be present under
# ./artifacts/ for multi-arch builds; single-arch builds need only the
# matching one.
COPY artifacts/ /staging/
RUN set -eu; \
    rid="$(cat /tmp/RID)"; \
    src="/staging/${rid}-selfcontained"; \
    if [ ! -d "$src" ]; then \
        echo "missing publish directory: $src" >&2; \
        ls -la /staging/ >&2; \
        exit 1; \
    fi; \
    cp -a "$src"/. /opt/ivi-cli/; \
    chmod +x /opt/ivi-cli/ivicli; \
    rm -rf /staging

# Pre-baked configuration + sample scenario. Operators override at
# runtime via `-v ./my-config.toml:/etc/ivi-cli/config.toml` or
# `-v ./my-scenarios:/etc/ivi-cli/scenarios`.
COPY docker/etc/ivi-cli/ /etc/ivi-cli/
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# Container-facing env wires the CLI to the pre-baked layout:
# - IVICLI_CONFIG     → config.toml location
# - IVICLI_MOCK_ONLY  → null out transport backends so every gateway
#                       device resolves to FakeBackend (ADR 0018 §2)
# - IVICLI_SCENARIO     → activate the named scenario on FakeBackend
# - IVICLI_SCENARIO_FOR → bind IVICLI_SCENARIO to this device alias
#                         (v0.2.4+ replaced the single-global-scenario
#                         model with per-device bindings; the container
#                         pins the baked `default` scenario to the
#                         baked `mock1` device — ADR 0026 §16).
ENV IVICLI_CONFIG=/etc/ivi-cli/config.toml \
    IVICLI_MOCK_ONLY=1 \
    IVICLI_SCENARIO=default \
    IVICLI_SCENARIO_FOR=mock1 \
    PATH=/opt/ivi-cli:${PATH}

# HiSlip gateway (per ADR 0018 §3) on 4880, SOCKET gateway on 5025.
EXPOSE 4880 5025

# Health: both gateway TCP listeners must accept a TCP probe.
HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD nc -z 127.0.0.1 4880 && nc -z 127.0.0.1 5025 || exit 1

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
