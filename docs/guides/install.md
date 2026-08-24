# Installing ivi-cli

Four ways in, and the one you want depends on a single question: do you
want ivi-cli to depend on a .NET installation, or not?

| | Needs .NET installed | Best for |
| --- | --- | --- |
| [Self-contained binary](#self-contained-binary) | no | most people, CI images, machines you do not control |
| [.NET tool](#net-tool) | yes — see the prerequisite below | machines that already build .NET |
| [mise](#mise) | no | pinning a version per project |
| [Container](#container) | no | running the mock instrument, not the CLI |

## Self-contained binary

Every release carries a `*-selfcontained.zip` per platform. The `ivicli`
inside it is one file with the runtime bundled, so it runs on a machine
with no .NET at all.

```sh
version=0.3.1
curl -fsSL -o ivicli.zip \
  "https://github.com/ShortArrow/ivi-cli/releases/download/v${version}/ivicli-${version}-linux-x64-selfcontained.zip"
unzip -j ivicli.zip ivicli -d ~/.local/bin
chmod +x ~/.local/bin/ivicli
```

Swap `linux-x64` for `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`,
or `win-arm64`. The `-j` flag keeps the debug symbols and XML docs that
travel in the archive out of your `bin` directory.

The other archive per platform, `*-fxdep.zip`, is the
framework-dependent build. It is smaller and needs .NET installed, so
reach for it only if you already meet the prerequisite below and want
the smaller download.

## .NET tool

```sh
dotnet tool install -g ivi-cli
```

**Prerequisite: the ASP.NET Core 10 runtime**, not just the base .NET
runtime. `ivicli api start` embeds an ASP.NET Core listener, and the
framework reference reaches the whole tool, so the requirement applies
even if you never run the API.

The official .NET SDK and the "ASP.NET Core Runtime" installer both
include it, which is why this rarely comes up on Windows or macOS.
Distributions that split the runtimes into separate packages are where
it bites:

| Distribution | Package |
| --- | --- |
| Arch, EndeavourOS, Manjaro | `aspnet-runtime` |
| Debian, Ubuntu | `aspnetcore-runtime-10.0` |
| Fedora, RHEL | `aspnetcore-runtime-10.0` |
| Alpine | `aspnetcore10-runtime` |

If the tool refuses to start, jump to
[When the .NET tool will not start](#when-the-net-tool-will-not-start).

## mise

mise installs from the GitHub release assets. Two tool options are
required, and without them you get the wrong thing or nothing at all:

```toml
[tools]
"ubi:ShortArrow/ivi-cli" = { version = "0.3.1", exe = "ivicli", matching = "selfcontained" }
```

- `exe = "ivicli"` — the backend looks for an executable named after the
  repository (`ivi-cli`), and the binary is `ivicli` without the hyphen.
  Omit this and the install fails with `could not find any files
  matching [ivi-cli*.bat ivi-cli*.exe]`.
- `matching = "selfcontained"` — two archives match every platform, and
  the backend otherwise picks `-fxdep`, the build that needs .NET
  installed. Omit this and you inherit the prerequisite above without
  being told.

mise has deprecated its `ubi` backend in favour of `github`, with
removal announced for 2027.1. The `github` backend takes the same
options; verify the resolved asset with `MISE_VERBOSE=1 mise install`
before trusting it, because asset selection is what goes wrong here.

## Container

The container runs the mock instrument, not the CLI. It is the right
answer for testing a VISA client without hardware and the wrong answer
for driving a real instrument.

```sh
docker run --rm -p 4880:4880 -p 5025:5025 -p 111:111 -p 111:111/udp \
  ghcr.io/shortarrow/ivi-cli-mock
```

Add `:aot` to the image for the NativeAOT build: same mock, same
gateways, roughly a third of the download and a much faster start. The
unsuffixed tags stay on the ordinary build.
[Mock a VISA instrument](mock-a-visa-instrument.md) covers what to do
with it once it is up.

## When the .NET tool will not start

```
You must install or update .NET to run this application.
Framework: 'Microsoft.AspNetCore.App', version '10.0.0' (x64)
No frameworks were found.
```

`dotnet --version` does not answer this: it reports the SDK, and the SDK
being present says nothing about which shared frameworks are installed.
Ask the question that matters:

```sh
dotnet --list-runtimes
```

**Only `Microsoft.NETCore.App` is listed.** The ASP.NET Core runtime is
missing. Install it from the table above — on Arch,
`sudo pacman -S aspnet-runtime`.

**Nothing is listed, or the paths differ from the ".NET location" in the
error.** The launcher and the runtimes disagree about where .NET lives.
Point it at the real one:

```sh
export DOTNET_ROOT=/usr/share/dotnet   # wherever --list-runtimes reported
```

**Neither, and you would rather not care.** Use the self-contained
binary. It carries its own runtime and cannot fail this way.
