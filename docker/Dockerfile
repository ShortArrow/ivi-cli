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

ARG RID=linux-x64
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

ARG RID

# `netcat-openbsd` provides the `nc` HEALTHCHECK probe; `bash` is the
# entrypoint shell. `--no-install-recommends` keeps the layer minimal.
RUN apt-get update \
    && apt-get install -y --no-install-recommends bash netcat-openbsd \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/ivi-cli

# Copy the self-contained publish output. The release.yml `publish` job
# emits into `./artifacts/<rid>-selfcontained/`; local invocations follow
# the same convention.
COPY artifacts/${RID}-selfcontained/ ./
RUN chmod +x ./ivicli

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
# - IVICLI_SCENARIO   → activate the named scenario on FakeBackend
ENV IVICLI_CONFIG=/etc/ivi-cli/config.toml \
    IVICLI_MOCK_ONLY=1 \
    IVICLI_SCENARIO=default \
    PATH=/opt/ivi-cli:${PATH}

# HiSlip gateway (per ADR 0018 §3) on 4880, SOCKET gateway on 5025.
EXPOSE 4880 5025

# Health: both gateway TCP listeners must accept a TCP probe.
HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
    CMD nc -z 127.0.0.1 4880 && nc -z 127.0.0.1 5025 || exit 1

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
