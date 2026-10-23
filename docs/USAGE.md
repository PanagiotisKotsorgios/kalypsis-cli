# KALYPSIS CLI 0.1.0 — Backoffice guide

[English](USAGE.md) · [Ελληνικά](USAGE.el.md)

Run `kalypsis.exe` without arguments for the Greek menu, or use commands below.
Use quotes around names or file paths containing spaces. Commands/options are
English; human-readable messages are Greek. `--json` emits stable English field
names and numeric JSON values suitable for scripts.

## Local database

The database is created automatically on the first data command. Its default
location is `%LOCALAPPDATA%\KalypsisCli\backoffice.db`. Set `KALYPSIS_CLI_DB` or
pass `--db "D:\Office\backoffice.db"` to choose another file. `--db` takes priority.
`init` creates an empty database explicitly. Help/version do not create data.

This is a single-user local MVP, not the Desktop/MySQL database and not an API client.
There are no cloud credentials or sync. Data is not encrypted by the CLI; use an
appropriately protected local account/storage and keep backups separately.
Opening an unrelated SQLite database is rejected instead of adopting its tables.

## Customers, insurers and partners

```powershell
.\kalypsis.exe customers add --first-name "Anna" --last-name "Test" --phone 6912345678 --email anna@example.com --city Athens
.\kalypsis.exe customers add --kind company --name "Example office" --vat 123456789
.\kalypsis.exe insurers add --name "Example carrier" --phone 2101234567
.\kalypsis.exe partners add --name "Example partner" --commission 10
.\kalypsis.exe customers list --search anna --limit 100
.\kalypsis.exe customers show C-000001
.\kalypsis.exe customers update C-000001 --phone 6912345670
.\kalypsis.exe customers history C-000001 --json
.\kalypsis.exe customers delete C-000002 --yes
```

Generated code prefixes: `C-` customers, `I-` insurers, `A-` partners, `P-` policies,
`R-` receipts. Every entity also has a numeric ID. Numeric IDs take precedence if
a custom code is numeric. Optional `--code` is accepted at creation; codes cannot
be changed later. `--commission` is the partner's default percentage for new policies.

`person` customers require `--first-name` and `--last-name`; `company` customers
require `--name`. Common contact fields: `--phone`, `--email`. Customers additionally
support `--vat`, `--city`. AFM validation checks nine digits, not official tax validity.
Updates preserve unspecified fields; `--phone ""` clears an optional field in shells
that pass empty arguments. PowerShell also accepts `--phone=`.

## Policies

```powershell
.\kalypsis.exe policies add --number POL001 --customer C-000001 --insurer I-000001 --partner A-000001 --start 2026-01-01 --end 2026-12-31 --gross 100 --net 80 --office-rate 6 --partner-rate 10 --branch Motor --plate ABC1234
.\kalypsis.exe policies list --customer C-000001 --status active
.\kalypsis.exe policies list --from 2026-01-01 --to 2026-12-31 --insurer I-000001
.\kalypsis.exe policies update P-000001 --plate ABC5678
.\kalypsis.exe policies renew P-000001 --number POL002 --start 2027-01-01 --end 2027-12-31 --gross 110 --net 85
.\kalypsis.exe policies cancel P-000002 --reason "Cancelled by carrier" --yes
```

Required on creation: number, customer, insurer, start, end, gross, net. Dates use
`yyyy-MM-dd` and are inclusive. New policies default to EUR. Optional fields are
partner, branch, plate, currency, office-rate and partner-rate. A number is unique
within its insurer. New receipts may not exceed the outstanding premium.

`--office-rate` is the percentage **retained by the office** on the net premium,
not the carrier's total commission before partner allocation. `--partner-rate`
is the partner's independent share on the same base. Their sum may not exceed 100%.
If omitted, the partner rate comes from the selected partner; office rate defaults
to zero. This MVP does not import the commercial applications' commission rules.

Active/future/expired display status is derived from local date and policy dates.
Filters: active, expired, scheduled, cancelled, renewed. List date filters refer
to policy start. Renewal creates a new policy, keeps the old record, links them
and prevents a second renewal from the same old policy. Cancellation records
status/history only; **it does not issue refunds or reverse cash receipts**.

## Receipts and summary

```powershell
.\kalypsis.exe receipts add --policy P-000001 --amount 50 --method bank --date 2026-02-01 --number REC001
.\kalypsis.exe receipts list --policy P-000001 --from 2026-01-01 --to 2026-12-31
.\kalypsis.exe receipts void R-000001 --reason "Incorrect amount" --yes
.\kalypsis.exe policies show P-000001 --json
.\kalypsis.exe summary
```

Money uses integer cents in SQLite. Enter `100.50` or `100,50`, without thousands
separators, with at most two decimals. Negative, zero receipt amounts and
scientific notation are rejected. Methods: cash, bank (default), card, other.
Receipt date defaults to today; number is generated if omitted. Currency comes
from the linked policy. Posted receipts count toward balances; void receipts
stay in history and no longer count. Voiding is an entry correction, not a bank refund.

Office commission becomes collected proportionally to actual premium receipts:
for gross 100/net 80/office rate 20%, collecting 50 recognises 8 of 16 commission.
It is income, not a separate payment debt. Summaries keep currencies separate.
Production/outstanding/office income exclude cancelled policies. Cash received
includes historical receipts even if a policy is later cancelled. Active count
includes only currently effective policies. Historical production includes renewals.
Partner payout settlement is outside this MVP.

## Export, backup and history

```powershell
.\kalypsis.exe export policies --insurer I-000001 --from 2026-01-01 --output production.csv
.\kalypsis.exe backup --output "D:\Backups\office-2026-10-23.db"
.\kalypsis.exe policies history P-000001 --json
```

Exports contain **all matching records**, independent of the list's `--limit`.
CSV uses semicolons, UTF-8 BOM, Greek labels and decimal formatting for Excel.
Spreadsheet formula-like values are escaped as text. Existing export/backup files
are never overwritten. Backup uses SQLite's backup API, including committed WAL
data, rather than copying a possibly open database file. Open the backup with
`--db <backup.db>` to verify it; there is no destructive restore command.

Customers, insurers and partners with policies cannot be deleted. Policies with
receipts (including void entries) or renewal links cannot be deleted. Unlinked
mistaken entries can be deleted with `--yes`. Receipt changes use void + a new
entry. Audits retain before/after snapshots, including after an unlinked deletion.

## Scripting

Exit codes: `0` success, `2` invalid input/action blocked, `3` storage/SQLite failure.
JSON successes go to stdout; JSON errors go to stderr. Human-readable lists show
50 records by default; use `--limit 1..1000`. Use `show` or JSON for untruncated
fields. No production/customer data is included in releases or tests.
