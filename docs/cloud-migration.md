# Cloud migration tool (Guardian deployment gate closed)

This entry point is separate from CoreDatabaseHarness and the API. It never loads
User Secrets or IConfiguration, never seeds, never resumes partial initial DDL,
and leaves ApplicationDbContextFactory's offline connection interceptor intact.

No arguments prints help without reading credentials or opening a connection.
`apply` currently fails BEFORE credential input/connection. There is deliberately
no force flag/environment bypass. The dormant Migrate path has been removed:
this version contains no executable Apply/Seed operation. Guardian integration,
transaction-independent MySQL DDL recovery, approved SQL and explicit interactive
confirmation must be designed/reviewed before a future Apply implementation.

## Offline commands

```powershell
dotnet run --project tests/CloudMigrationSafety.csproj
dotnet run --project tools/CloudMigration -- script --output migration-review.sql
```

Script is EF-generated **0 -> latest**, not a pending-only delta. Existing output
files cannot be overwritten. It uses the offline factory/version and does not
connect. Review SQL against MySQL 8.4.11 before execution; no compatibility guarantee.

## Explicit read-only database modes (not executed during development)

After separately approved SSH setup, set these process environment variables:

```powershell
$env:EARLYCARE_DEPLOY_HOST = '127.0.0.1'
$env:EARLYCARE_DEPLOY_PORT = '13306'
$env:EARLYCARE_DEPLOY_DATABASE = 'earlycare_dev'
$env:EARLYCARE_DEPLOY_USER = 'earlycare_migration'
dotnet run --project tools/CloudMigration -- check
dotnet run --project tools/CloudMigration -- history
dotnet run --project tools/CloudMigration -- preflight
```

Password is entered interactively with hidden keystrokes, or supplied through a
trusted process-only `EARLYCARE_DEPLOY_PASSWORD`. Never put it in a shell command,
history, Git, report or CI output. Clear password environment variables after use.
Do not use CoreDatabaseHarness --configure: it overwrites local DefaultConnection.

Only this exact host/port/database/account is accepted; no arbitrary connection
string. This restricts the local endpoint, but does not prove the tunnel points
to the approved VM. Verify SSH host fingerprint and tunnel destination separately.
The tool never creates a tunnel or changes public firewall rules.

`check` validates exact server version, selected DB and Linux case-sensitive names.
`history` validates history as a prefix of the compiled migrations; partial initial
schema stops. `preflight` also checks tables and column type/nullability against the
last applied migration. Structural comparison includes extra/missing columns,
defaults, FK columns/order/target/delete/update actions, index names/uniqueness/
column order, and charset/collation. Comparison is conservative: server-normalized
defaults or aliases can be refused rather than repaired. It is NOT a complete
schema-equivalence proof: generated/check expressions, index prefix length/type/
direction/visibility, triggers, table options, history table definition/content
provenance and metadata query truncation still require human review. An empty DB
has no existing schema to validate; inspect the generated SQL instead.

The endpoint check cannot establish MySQL 8.4 provider compatibility. Pomelo 8.0.3
release explicitly lists MySQL 8.0. Real 8.4.11 apply/rollback recovery, auth plugin,
case sensitivity, composite FK and index compatibility remain untested.

## Guardian integration checklist

1. Agree on deployed commit/model scope; obtain and review Guardian branch.
2. Preserve the four published migrations; add reviewed integration migration.
3. Verify snapshot/model and migration chain, and review existing DB schema/history.
4. Review metadata and the generated baseline SQL in a disposable 8.4.11 DB.
   This tool does not generate delta SQL.
5. Implement/review/test future Apply confirmation and remaining drift checks.
6. Provide backup/recovery and durable apply audit before enabling the compiled gate.

Deployment report: commit, target DB/version, before/after history, pending IDs,
table/FK/index review, provider compatibility result, seed policy, failures and
recovery status. Never include credentials or business rows.

## Offline entry tests and safe errors

Program delegates directly to DeploymentEntry.RunAsync. Tests execute this same
entry with injected spies for environment/password/context/connection. No database
factory or connection is allowed in tested help/apply/script paths. Fake metadata
tests cover defaults, extra columns, FK/index changes and encoding changes. The SQL
writer uses CreateNew and deletes only its own newly-created incomplete file after
failure; cleanup failure reports SQL_SCRIPT_CLEANUP_FAILED (manual removal required).

Safe codes: APPLY_BLOCKED, INVALID_ARGUMENT, INVALID_ENDPOINT,
INVALID_DATABASE_VERSION, MIGRATION_HISTORY_MISMATCH, SCHEMA_DRIFT,
SQL_SCRIPT_WRITE_FAILED. Unexpected failures use CHECK_FAILED; original exceptions
and credentials are never printed. Since no Apply operation exists in this version,
failure correctly states no Migration executed. Any future write-capable version
must track the start of DDL and report possible partial changes, not reuse this text.
