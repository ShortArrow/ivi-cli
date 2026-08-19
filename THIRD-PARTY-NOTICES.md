# Third-party notices

`ivicli` itself is licensed MIT OR Apache-2.0 — `LICENSE-MIT` and
`LICENSE-APACHE` carry the two grants. It does not ship alone: every release
archive, the container image, and the `dotnet tool` package place third-party
assemblies beside the executable. This file is the attribution their licenses
ask to travel with those copies, and it is distributed with them.

Package metadata is not a sufficient source for it, so two things are worth
saying about how the entries were established:

- Four packages declare no license at all in their NuGet metadata
  (`Common.Logging`, `Common.Logging.Core`, `Makaretu.Dns`,
  `Makaretu.Dns.Multicast`) and two declare only a URL (`SimpleBase`,
  `IPNetwork2`). Their terms below were read from the projects' own
  repositories, cited in each entry, rather than scraped from the packages.
- The set of entries is compared against `src/IviCli.Cli/packages.lock.json`
  on every pull request. A package that enters the dependency closure without
  an entry here — or an entry left behind by a package that has left it —
  fails that check.

Versions are deliberately absent. An entry attributes a work, not a release,
so a dependency bump cannot silently make this file wrong.

## IVI Foundation License Agreement

- `IviFoundation.Visa` — Copyright (c) IVI Foundation, Inc, 2011-2025. All
  Rights Reserved.

This is the one grant here conditioned on redistribution carrying it: the
permission below holds "provided that the above copyright notice(s) appear in
all copies". ivi-cli redistributes `Ivi.Visa.dll` unmodified, and as a
non-member of the Foundation is licensed for its object code.

```
LICENSE AGREEMENT

Readers of this document are requested to submit to Interchangeable Virtual Instruments, Inc. ("Licensor"), with their comments, notification of any relevant patent rights or other intellectual property rights of which they may be aware which might be infringed by any use of this intellectual property, software, or specification (the "Intellectual Property"), as appropriate, and to provide supporting documentation.

Copyright (C) 2002, Interchangeable Virtual Instruments Foundation, Inc.  All Rights Reserved.  IVI Foundation is the exclusive licensee of the "IVI" trademark and the Interchangeable Virtual Instruments Foundation, Inc. logo.

Attention is drawn to the possibility that some of the elements of this Intellectual Property may be the subject of patent or other intellectual property right (collectively, "IPR") of third parties. LICENSOR shall not be responsible now or in the future for identifying any or all such IPR.

Permission is hereby granted, free of charge and subject to the terms set forth below, to any person obtaining a copy of this Intellectual Property and any associated documentation, to deal in the Intellectual Property without restriction (except as set forth below), including without limitation the rights to implement, use, copy, modify, merge, publish, distribute, and/or sublicense copies of the Intellectual Property, and to permit persons to whom the Intellectual Property is furnished to do so, provided that the above copyright notice(s) appear in all copies of the Intellectual Property and that each person to whom the Intellectual Property is furnished agrees to the terms of this Agreement.  If you are not a member of LICENSOR, your license hereunder is limited to the use of the object code of the Intellectual Property and header files necessary to use the object code. If you are a member of LICENSOR, your license extends to the source code of the Intellectual Property.

If you modify the Intellectual Property, all copies of the modified Intellectual Property must include, in addition to the above copyright notice, a notice that the Intellectual Property includes modifications that have not been approved or adopted by LICENSOR.

You may not charge for any sublicense of the Intellectual Property; provided however, that the Intellectual Property may be sublicensed together with another product so long as there is no separate charge for the Intellectual Property.

THE INTELLECTUAL PROPERTY IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NONINFRINGEMENT OF THIRD PARTY RIGHTS.  THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE DO NOT WARRANT THAT THE FUNCTIONS CONTAINED IN THE INTELLECTUAL PROPERTY WILL MEET YOUR REQUIREMENTS OR THAT THE OPERATION OF THE INTELLECTUAL PROPERTY WILL BE UNINTERRUPTED OR ERROR FREE.  ANY USE OF THE INTELLECTUAL PROPERTY SHALL BE MADE ENTIRELY AT THE USER'S OWN RISK.  IN NO EVENT SHALL THE COPYRIGHT HOLDER OR ANY CONTRIBUTOR OF IPR TO THE INTELLECTUAL PROPERTY BE LIABLE FOR ANY CLAIM, OR ANY DIRECT, SPECIAL, INDIRECT OR CONSEQUENTIAL DAMAGES, OR ANY DAMAGES WHATSOEVER RESULTING FROM ANY ALLEGED INFRINGEMENT OR ANY LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR UNDER ANY OTHER LEGAL THEORY, ARISING OUT OF OR IN CONNECTION WITH THE IMPLEMENTATION, USE, COMMERCIALIZATION OR PERFORMANCE OF THIS INTELLECTUAL PROPERTY.

This license is effective until terminated. You may terminate it at any time by destroying the Intellectual Property together with all copies in any form. It will also terminate if you fail to comply with any term or condition of this Agreement.  Except as provided in the following sentence, no such termination of this license shall require the termination of any third-party end-user sublicense to the Intellectual Property which is in force as of the date of notice of such termination.  In addition, should the Intellectual Property, or the operation of the Intellectual Property, infringe, or in LICENSOR's sole opinion be likely to infringe, any patent, copyright, trademark or other right of a third party, you agree that LICENSOR, in its sole discretion, may terminate this license without any compensation or liability to you, your licensees or any other party.  You agree upon termination of any kind to destroy or cause to be destroyed the Intellectual Property together with all copies in any form, whether held by you or by any third party.

Except as contained in this notice, the name of a copyright holder shall not be used in advertising or otherwise to promote the sale, use or other dealings in this Intellectual Property without prior written authorization of the copyright holder.  LICENSOR is and shall at all times be the sole entity that may authorize you or any third party to use certification marks, trademarks or other special designations to indicate compliance with any LICENSOR standards or specifications.

This Agreement is governed by the laws of the State of Delaware.  The application to this Agreement of the United Nations Convention on Contracts for the International Sale of Goods is hereby expressly excluded. In the event any provision of this Agreement shall be deemed unenforceable, void or invalid, such provision shall be modified so as to make it valid and enforceable, and as so modified the entire Agreement shall remain in full force and effect.  No decision, action or inaction by LICENSOR shall be construed to be a waiver of any rights or remedies available to it.

The Intellectual Property is a "commercial item," as that term is defined in 48 C.F.R. 12.101 (Oct. 1995), consisting of "commercial computer software" and "commercial computer software documentation," as such terms are used in 48 C.F.R. 12.212 (Sept. 1995).  Consistent with 48 C.F.R. 12.212 and 48 C.F.R. 227.7202-1 through 227.7202-4 (June 1995), all U.S. Government End Users acquire the Intellectual Property with only those rights set forth herein.

None of the Intellectual Property or underlying information or technology may be downloaded or otherwise exported or reexported in violation of U.S. export laws and regulations.  In addition, you are responsible for complying with any local laws in your jurisdiction which may impact your right to import, export or use the Intellectual Property, and you represent that you have complied with any regulations or registration procedures required by applicable law to make this license enforceable.
```

## Apache License, Version 2.0

- `Common.Logging` — Copyright © 2002-2009 the original author or authors.
  License read from the project repository
  (<https://github.com/net-commons/common-logging>); the package declares none.
- `Common.Logging.Core` — same work and same source as above.
- `OpenTelemetry` — Copyright The OpenTelemetry Authors
- `OpenTelemetry.Api` — Copyright The OpenTelemetry Authors
- `OpenTelemetry.Api.ProviderBuilderExtensions` — Copyright The OpenTelemetry Authors
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` — Copyright The OpenTelemetry Authors
- `OpenTelemetry.Extensions.Hosting` — Copyright The OpenTelemetry Authors
- `OpenTelemetry.Instrumentation.AspNetCore` — Copyright The OpenTelemetry Authors
- `Serilog` — Copyright © Serilog Contributors
- `Serilog.Extensions.Hosting` — Copyright © Serilog Contributors
- `Serilog.Extensions.Logging` — Copyright © Serilog Contributors
- `Serilog.Formatting.Compact` — Copyright © Serilog Contributors
- `Serilog.Sinks.Console` — Copyright © Serilog Contributors
- `Serilog.Sinks.File` — Copyright © Serilog Contributors
- `SimpleBase` — Copyright 2014-2017 Sedat Kapanoglu. License read from the
  project repository (<https://github.com/ssg/SimpleBase>); the package
  declares only a URL.

The complete license text is the `LICENSE-APACHE` file distributed alongside
this one. It is there as ivi-cli's own Apache grant, and the terms above are
that same document; only its appendix names ivi-cli. None of the works above
carries a NOTICE file, so section 4(d) adds nothing to reproduce here, and
none of them is redistributed modified.

## MIT License

- `Makaretu.Dns` — Copyright (c) 2018 Richard Schneider. License read from the
  project repository (<https://github.com/richardschneider/net-dns>); the
  package declares none.
- `Makaretu.Dns.Multicast` — Copyright (c) 2018 Richard Schneider. License read
  from the project repository
  (<https://github.com/richardschneider/net-mdns>); the package declares none.
- `Microsoft.AspNetCore.OpenApi`
- `Microsoft.Extensions.Configuration`
- `Microsoft.Extensions.Configuration.Abstractions`
- `Microsoft.Extensions.Configuration.Binder`
- `Microsoft.Extensions.Configuration.CommandLine`
- `Microsoft.Extensions.Configuration.EnvironmentVariables`
- `Microsoft.Extensions.Configuration.FileExtensions`
- `Microsoft.Extensions.Configuration.Json`
- `Microsoft.Extensions.Configuration.UserSecrets`
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Diagnostics`
- `Microsoft.Extensions.Diagnostics.Abstractions`
- `Microsoft.Extensions.FileProviders.Abstractions`
- `Microsoft.Extensions.FileProviders.Physical`
- `Microsoft.Extensions.FileSystemGlobbing`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging`
- `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.Extensions.Logging.Configuration`
- `Microsoft.Extensions.Logging.Console`
- `Microsoft.Extensions.Logging.Debug`
- `Microsoft.Extensions.Logging.EventLog`
- `Microsoft.Extensions.Logging.EventSource`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Options.ConfigurationExtensions`
- `Microsoft.Extensions.Primitives`
- `Microsoft.NETCore.Platforms`
- `Microsoft.OpenApi`
- `NETStandard.Library`
- `System.CommandLine`
- `System.Diagnostics.EventLog`
- `Spectre.Console` — Copyright (c) Patrik Svensson, Phil Scott, Nils Andresen,
  Cédric Luthi
- `Spectre.Console.Ansi` — Copyright (c) Patrik Svensson, Phil Scott, Nils
  Andresen, Cédric Luthi
- `TestableIO.System.IO.Abstractions` — Copyright © Tatham Oddie & friends
  2010-2026
- `TestableIO.System.IO.Abstractions.Wrappers` — Copyright © Tatham Oddie &
  friends 2010-2026
- `Testably.Abstractions.FileSystem.Interface` — Copyright (c) 2024-2026
  Testably
- `Tmds.LibC` — Copyright (c) Tom Deseyn

Entries above carrying no copyright line of their own are © Microsoft
Corporation. All rights reserved. Two of those — `Microsoft.NETCore.Platforms`
and `NETStandard.Library` — contribute no file to any distribution; they are
listed because they sit in the dependency closure the check reads, and
attributing them costs nothing.

The MIT License, which applies to each work above with that entry's own
copyright notice substituted:

```
MIT License

Copyright (c) <copyright holder>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## BSD 2-Clause License

- `Tomlyn` — Copyright (c) 2020, Alexandre Mutel

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## BSD 3-Clause License

- `IPNetwork2` — Copyright (c) 2015, lduchosal. License read from the project
  repository (<https://github.com/lduchosal/ipnetwork>); the package declares
  only a URL.

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Beyond the managed packages

Two of the distributions carry more than the assemblies listed above.

The self-contained archives and the container image bundle the .NET runtime
itself — Copyright (c) .NET Foundation and Contributors, MIT — whose own
third-party attribution is published with it as `THIRD-PARTY-NOTICES.TXT` in
<https://github.com/dotnet/runtime>.

The container image additionally layers Debian packages from its
`mcr.microsoft.com/dotnet/runtime-deps` base. Those licenses are not
reproduced here; they are inside the image, under `/usr/share/doc`, where the
Debian tooling puts them.
