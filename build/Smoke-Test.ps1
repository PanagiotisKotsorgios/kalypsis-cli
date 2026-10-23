[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$testRoot = Join-Path $workspace ('artifacts\smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$dbFile = Join-Path $testRoot 'backoffice.db'
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $workspace 'artifacts\bundle-cache'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$script:checks = 0
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Invoke-Cli([string[]]$CommandArgs, [int]$ExpectedExit = 0, [string]$Database = $dbFile) {
    $ErrorActionPreference = 'Continue'
    $errorFile = Join-Path $testRoot 'last-error.txt'
    $raw = & $Executable @CommandArgs --db $Database --json 2> $errorFile
    $code = $LASTEXITCODE
    if ($code -ne $ExpectedExit) { throw "Unexpected exit code $code instead of $ExpectedExit for $($CommandArgs -join ' '). $(Get-Content $errorFile -Raw)" }
    $script:checks++
    if ($ExpectedExit -eq 0) { return (($raw -join "`n") | ConvertFrom-Json) }
}
$version = Invoke-Cli -CommandArgs @('--version'); Check ($version.version -eq '0.2.0') 'Version mismatch'
$null = Invoke-Cli -CommandArgs @('init')
$customer = (Invoke-Cli -CommandArgs @('customers','add','--first-name','Μαρία','--last-name','Δοκιμή','--city','Αθήνα','--phone','6912345678')).records[0]
Check ($customer.name -eq 'Μαρία Δοκιμή') 'Unicode customer name was not preserved'
$insurer = (Invoke-Cli -CommandArgs @('insurers','add','--name','Ασφαλιστική δοκιμής')).records[0]
$partner = (Invoke-Cli -CommandArgs @('partners','add','--name','Συνεργάτης δοκιμής','--commission','10')).records[0]
$policy = (Invoke-Cli -CommandArgs @('policies','add','--number','SMOKE-POL-1','--customer',$customer.code,'--insurer',$insurer.code,'--partner',$partner.code,'--start','2026-01-01','--end','2026-12-31','--gross','100','--net','80','--office-rate','20')).records[0]
Check ($policy.office_expected -eq 16 -and $policy.partner_expected -eq 8) 'Commission rates are incorrect'
$receipt = (Invoke-Cli -CommandArgs @('receipts','add','--policy',$policy.code,'--amount','40','--number','SMOKE-R-1')).records[0]
$partial = (Invoke-Cli -CommandArgs @('policies','show',$policy.code)).records[0]
Check ($partial.outstanding -eq 60 -and $partial.office_received -eq 6.4) 'Partial receipt totals are incorrect'
$null = Invoke-Cli -CommandArgs @('receipts','add','--policy',$policy.code,'--amount','60')
$paid = (Invoke-Cli -CommandArgs @('policies','show',$policy.code)).records[0]
Check ($paid.outstanding -eq 0 -and $paid.office_received -eq 16) 'Full receipt totals are incorrect'
$null = Invoke-Cli -CommandArgs @('receipts','add','--policy',$policy.code,'--amount','0.01') -ExpectedExit 2
$null = Invoke-Cli -CommandArgs @('customers','delete',$customer.code,'--yes') -ExpectedExit 2
$null = Invoke-Cli -CommandArgs @('receipts','void',$receipt.code,'--reason','Λανθασμένη δοκιμαστική εγγραφή','--yes')
$voided = (Invoke-Cli -CommandArgs @('policies','show',$policy.code)).records[0]
Check ($voided.outstanding -eq 40 -and $voided.office_received -eq 9.6) 'Voiding did not update balances'
$credit = (Invoke-Cli -CommandArgs @('credits','add','--policy',$policy.code,'--amount','50','--net','40','--reason','Δοκιμαστικό μερικό πιστωτικό')).records[0]
Check ($credit.reason -eq 'Δοκιμαστικό μερικό πιστωτικό' -and $credit.amount -eq 50 -and $credit.net -eq 40) 'Credit values or Unicode reason lost'
$creditedPolicy = (Invoke-Cli -CommandArgs @('policies','show',$policy.code)).records[0]
Check ($creditedPolicy.chargeable -eq 50 -and $creditedPolicy.customer_credit -eq 10 -and $creditedPolicy.outstanding -eq 0 -and $creditedPolicy.office_received -eq 8) 'Credit balances or commissions incorrect'
$null = Invoke-Cli -CommandArgs @('credits','add','--policy',$policy.code,'--amount','50.01','--reason','Over-credit') -ExpectedExit 2
$null = Invoke-Cli -CommandArgs @('credits','void',$credit.code,'--reason','Missing confirmation') -ExpectedExit 2
$creditCsv = Join-Path $testRoot 'credits.csv'
$null = Invoke-Cli -CommandArgs @('export','credits','--policy',$policy.code,'--output',$creditCsv)
Check (([IO.File]::ReadAllLines($creditCsv)).Length -eq 2) 'Credit export did not follow its filter'
$creditBackup = Join-Path $testRoot 'with-credit.db'
$null = Invoke-Cli -CommandArgs @('backup','--output',$creditBackup)
$savedCredit = (Invoke-Cli -CommandArgs @('credits','show',$credit.code) -Database $creditBackup).records[0]
Check ($savedCredit.status -eq 'posted' -and $savedCredit.amount -eq 50) 'Credit missing from backup'
$null = Invoke-Cli -CommandArgs @('credits','void',$credit.code,'--reason','Ακύρωση δοκιμής','--yes')
$creditHistory = Invoke-Cli -CommandArgs @('credits','history',$credit.code)
Check ($creditHistory.count -eq 2) 'Credit audit history incomplete'
$restored = (Invoke-Cli -CommandArgs @('policies','show',$policy.code)).records[0]
Check ($restored.outstanding -eq 40 -and $restored.customer_credit -eq 0 -and $restored.office_received -eq 9.6) 'Voiding credit did not restore totals'
$renewed = (Invoke-Cli -CommandArgs @('policies','renew',$policy.code,'--number','SMOKE-POL-2','--start','2027-01-01','--end','2027-12-31')).records[0]
Check ($renewed.previous_id -eq $policy.id -and $renewed.partner_rate -eq 10) 'Renewal lost its source or rates'
$history = Invoke-Cli -CommandArgs @('policies','history',$policy.code)
Check ($history.count -ge 2) 'Renewal is absent from history'
$filtered = Invoke-Cli -CommandArgs @('customers','list','--search','μαρια')
Check ($filtered.count -eq 1) 'Greek case/accent-insensitive search failed'
$csv = Join-Path $testRoot 'policies.csv'
$null = Invoke-Cli -CommandArgs @('export','policies','--search','SMOKE-POL-1','--output',$csv)
$bytes = [IO.File]::ReadAllBytes($csv)
Check ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) 'CSV BOM missing'
Check (([IO.File]::ReadAllLines($csv)).Length -eq 2) 'Export did not follow the current filter'
$backup = Join-Path $testRoot 'backup.db'
$null = Invoke-Cli -CommandArgs @('backup','--output',$backup)
$backupPolicy = (Invoke-Cli -CommandArgs @('policies','show',$policy.code) -Database $backup).records[0]
Check ($backupPolicy.outstanding -eq 40) 'Backup did not contain committed data'
$summary = Invoke-Cli -CommandArgs @('summary')
Check ($summary.records[0].currency -eq 'EUR') 'Summary currency missing'
$report = [ordered]@{ version='0.2.0'; executable=$Executable; checks=$checks; failed=0; database=$dbFile; checkedAtUtc=[DateTime]::UtcNow.ToString('o') }
[IO.File]::WriteAllText((Join-Path $testRoot 'results.json'), ($report | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "PASS: $checks checks of the packaged executable. Synthetic test data and report: $testRoot"
