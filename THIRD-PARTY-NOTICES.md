# Third-party components / Στοιχεία τρίτων

The Windows release includes the following components. Their licences apply to
their respective components, independently of the KALYPSIS project/brand.
Το πακέτο περιέχει τα παρακάτω στοιχεία, με τις δικές τους άδειες.

| Component | Version | Licence | Source |
| --- | --- | --- | --- |
| .NET runtime | 10.0.13 | MIT and runtime third-party notices | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite / Core | 10.0.13 | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw bundle/core/provider/native package | 2.1.13 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite engine | bundled by SQLitePCLRaw | Public domain | https://sqlite.org/copyright.html |

Microsoft.Data.Sqlite licence: [MIT text](docs/licenses/Microsoft.Data.Sqlite-MIT.txt).
SQLitePCLRaw licence: [Apache 2.0 text](docs/licenses/APACHE-2.0.txt).
The release ZIP includes these texts and the .NET runtime's licence/third-party
notices under `licenses/`. Runtime notices also cover runtime-bundled components.

Test-only dependencies are not bundled in the executable: xUnit 2.9.2 (Apache-2.0),
xUnit Visual Studio runner 2.8.2 (Apache-2.0), Microsoft.NET.Test.Sdk 17.11.1 (MIT).
They are restored from NuGet by the test project.
