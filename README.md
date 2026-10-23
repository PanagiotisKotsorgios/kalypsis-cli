<p align="center">
  <a href="https://mykalypsis.gr/"><img src="docs/assets/kalypsis-logo.png" alt="KALYPSIS insurance management platform" width="260" /></a>
</p>

<h1 align="center">KALYPSIS CLI</h1>

<p align="center">A public command-line project in the KALYPSIS ecosystem.</p>

<p align="center"><img src="docs/assets/project-badges.svg" alt="KALYPSIS · Subsystem · CLI · Windows" width="520" /></p>

[English](README.md) · [Ελληνικά](README.el.md)

[Website](https://mykalypsis.gr/) · [Repository](https://github.com/PanagiotisKotsorgios/kalypsis-cli) · [Contribute](CONTRIBUTING.md) · [Issues](https://github.com/PanagiotisKotsorgios/kalypsis-cli/issues)

**KALYPSIS CLI 0.2.0** is a small, standalone insurance-office backoffice for the
Windows terminal, written in **C# / .NET 10**, with a local **SQLite** database.
It works offline without a KALYPSIS Cloud account or a separate database server.

## Current status

The **0.2.0 terminal prototype** includes the core backoffice workflow:

- Customers, insurance companies and partners: create, search, view, update and delete unlinked records.
- Policies: gross/net premiums, customer/company/partner links, commission percentages, renewal history and cancellation status.
- Receipts: partial or full collection, overpayment protection, and voiding incorrect entries with an audit trail.
- Credit notes: partial or full policy credits, gross/net reconciliation, customer credit balances and auditable voiding.
- Financial summary by currency, filtered UTF-8 CSV exports and consistent database backups.
- Greek interactive menu and console messages; scriptable commands with `--json` and predictable exit codes.
- Navy KALYPSIS screens, colored actions and tables, paged/searchable lists, record views and financial warnings.

Interactive screens clear between actions and pause after results. Use `N`/`P` to
page, `F` to search, `C` to clear search and a record code to open its details.
Colors are disabled for redirected output, JSON, `NO_COLOR` and `TERM=dumb`.
Credit notes have a separate ledger and red/white financial indicators. Existing
0.1.0 databases are backed up automatically before the schema upgrade.

## Download and run

Download **[kalypsis-cli-0.2.0-win-x64.zip](https://github.com/PanagiotisKotsorgios/kalypsis-cli/releases/latest/download/kalypsis-cli-0.2.0-win-x64.zip)**
from the [latest release](https://github.com/PanagiotisKotsorgios/kalypsis-cli/releases/latest), extract it, open PowerShell in the extracted folder and run:

```powershell
.\kalypsis.exe
.\kalypsis.exe --help
.\kalypsis.exe --version
```

The Windows x64 package is self-contained; installing .NET separately is unnecessary.
There is no installer and no administrator requirement. Keep the extracted files together.

Default database: `%LOCALAPPDATA%\KalypsisCli\backoffice.db`.
Override with `--db <path>` or the `KALYPSIS_CLI_DB` environment variable.
Release packages contain **no customer records, sample database or credentials**.

## First records

```powershell
.\kalypsis.exe customers add --first-name "Μαρία" --last-name "Δοκιμή" --phone 6912345678
.\kalypsis.exe insurers add --name "Insurance company"
.\kalypsis.exe policies add --number POL001 --customer C-000001 --insurer I-000001 --start 2026-01-01 --end 2026-12-31 --gross 100 --net 80 --office-rate 20
.\kalypsis.exe receipts add --policy P-000001 --amount 50
.\kalypsis.exe policies show P-000001
.\kalypsis.exe summary --json
.\kalypsis.exe export customers --output customers.csv --search "Μαρία"
.\kalypsis.exe backup --output office-backup.db
```

Examples use synthetic names. IDs/codes are printed after creation; use your own returned codes.
See the [English guide](docs/USAGE.md) or [Ελληνικό οδηγό](docs/USAGE.el.md) for fields, financial rules and limitations.

## Build and test

Requires the **.NET 10 SDK**:

```powershell
dotnet restore Kalypsis.Cli.sln
dotnet build Kalypsis.Cli.sln -c Release --no-restore
dotnet test Kalypsis.Cli.sln -c Release --no-build
dotnet run --project src/Kalypsis.Cli -- --help
powershell -ExecutionPolicy Bypass -File build/Publish.ps1
```

Packaging writes to `artifacts/releases`. The ZIP contains the executable, usage guides,
third-party notices and checksums. Binaries and databases are not committed to Git.
The packaging script requires a clean, committed Git checkout and a fresh output directory.

KALYPSIS Web and Desktop are separate applications; this repository does not
contain their source code or grant access to their systems or customer data.
The MVP does not synchronise with them and does not include quotations, carrier bridges,
claims management, messaging, payments to beneficiaries or a customer portal.

## Screenshots

Actual KALYPSIS CLI 0.2.0 screens with synthetic demonstration data.

### Office overview

![KALYPSIS CLI office overview and backoffice menu](docs/assets/cli-office-overview.jpg)

### Customers list

![Searchable customers table with pagination](docs/assets/cli-customers-list.jpg)

### Customer card

![Customer details in the CLI](docs/assets/cli-customer-card.jpg)

### Policies list

![Policy table with premiums, outstanding balances and colored statuses](docs/assets/cli-policies-list.jpg)

### Policy card

![Policy details with premiums, credit notes and commission amounts](docs/assets/cli-policy-card.jpg)

## Project identity

`kalypsis` · `kalypsis-subsystem` · `cli` · `windows` · `insurance` · `csharp` · `dotnet` · `sqlite`

The KALYPSIS logo identifies the project ecosystem. Access to this repository
does not grant rights to the brand or to the separate commercial applications.
No open-source licence has been selected for this repository yet; public
visibility is not a software licence.

## Contribute

Read the [contribution guidelines](CONTRIBUTING.md) before opening an issue
or a pull request. Suggestions and documentation improvements are welcome.
Discuss implementation proposals with the maintainer first; do not assume
that a proposed feature, API or technical stack has been approved.

Never submit passwords, access tokens, production credentials or real
insurance/customer records. Use synthetic examples only.

## Links

- [KALYPSIS website](https://mykalypsis.gr/)
- [Project repository](https://github.com/PanagiotisKotsorgios/kalypsis-cli)
- [Contribution guidelines in English](CONTRIBUTING.md)
- [Οδηγίες συνεισφοράς στα Ελληνικά](CONTRIBUTING.el.md)
