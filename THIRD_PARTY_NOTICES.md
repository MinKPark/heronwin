# Third-Party Notices

This repository does not vendor third-party source code or binary packages.
Dependencies are restored from NuGet when the .NET projects are built or tested.

The dependency summary below was checked from NuGet package metadata for the
packages referenced by `src/heronwin.sln`.

## Runtime Dependencies

| Package family | Version(s) in use | License | Source |
| --- | --- | --- | --- |
| `Microsoft.Extensions.*` | `8.0.x`, `10.0.0` | MIT | <https://github.com/dotnet/runtime>, <https://github.com/dotnet/extensions> |
| `ModelContextProtocol`, `ModelContextProtocol.Core` | `0.5.0-preview.1` | MIT | <https://github.com/modelcontextprotocol/csharp-sdk> |
| `NAudio`, `NAudio.*` | `2.2.1` | MIT | <https://github.com/naudio/NAudio> |

## Test And Development Dependencies

| Package family | Version(s) in use | License | Source |
| --- | --- | --- | --- |
| `Microsoft.NET.Test.Sdk`, `Microsoft.CodeCoverage`, `Microsoft.TestPlatform.*` | `17.10.0` | MIT | <https://github.com/microsoft/vstest> |
| `Newtonsoft.Json` | `13.0.1` | MIT | <https://github.com/JamesNK/Newtonsoft.Json> |
| `xunit`, `xunit.*`, `xunit.runner.visualstudio` | `2.0.3`, `2.8.2`, `2.9.0`, `1.15.0` | Apache-2.0 | <https://github.com/xunit/xunit>, <https://github.com/xunit/visualstudio.xunit>, <https://github.com/xunit/xunit.analyzers> |

If a package is added, removed, or upgraded, refresh this file with:

```powershell
dotnet list src/heronwin.sln package --include-transitive
```
