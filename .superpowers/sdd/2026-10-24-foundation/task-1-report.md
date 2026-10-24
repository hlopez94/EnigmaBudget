# Task 1 report: backend, identity, PostgreSQL and onboarding

## Implemented

- Created the .NET 10 solution and API/test projects with `global.json` pinned to SDK 10.0.402.
- Added ASP.NET cookie authentication, official Google authentication handler wiring, fixed opt-in Development sign-in, logout, session/CSRF token issuance and request validation. `/api/auth/google` returns 503 when either credential is missing; the development route is only mapped in Development and also checks its explicit opt-in flag.
- Added EF Core 10/Npgsql persistence and a generated initial migration/model snapshot. Authenticated Google subjects own a unique personal workspace; the authenticated subject is the only ownership key used by API queries.
- Added onboarding draft JSON persistence, bounded partial-save checks, optimistic version conflicts, completion validation, transactionally persisted confirmed accounts/cards, separate opening-balance records and idempotent confirmation. Amounts use invariant strings at the API edge and `numeric(18,2)` in PostgreSQL; ARS and USD are preserved independently.
- Added authenticated account/card summary routes and Spanish ProblemDetails responses.
- Added real PostgreSQL integration tests. Each API fixture creates and drops a temporary schema; tests cover anonymous 401, fixed developer sign-in, CSRF enforcement, Production developer-login 404, unconfigured Google 503, initial version 0, version increments and stale 409, per-identity draft isolation, ARS/USD persistence, repeated confirmation, and invalid dates/amounts/currency/duplicate IDs.
- Added PostgreSQL test setup instructions in `tests/EnigmaBudget.Api.Tests/README.md`.

## TDD evidence

RED command:

```sh
PATH="/home/her38/Documentos/ChatGPT/App Gastos/work/tooling/dotnet:$PATH" dotnet test tests/EnigmaBudget.Api.Tests/EnigmaBudget.Api.Tests.csproj --no-restore
```

The first compilable run was before API behavior existed. It ran 9 tests and failed 8 on missing routes (including `/api/auth/session` returning 404 and authenticated onboarding behavior unavailable); this was the expected RED signal. One test passed. The test harness itself was corrected before recording this run so failures were behavioral rather than compile failures.

GREEN command:

```sh
PATH="/home/her38/Documentos/ChatGPT/App Gastos/work/tooling/dotnet:$PATH" dotnet test EnigmaBudget.sln --logger 'console;verbosity=minimal'
```

Result: **Passed, 12/12, 0 failed, 0 skipped** against PostgreSQL.

Build command:

```sh
PATH="/home/her38/Documentos/ChatGPT/App Gastos/work/tooling/dotnet:$PATH" dotnet build EnigmaBudget.sln --no-restore
```

Result: **Build succeeded, 0 warnings, 0 errors**.

## Files

- `EnigmaBudget.sln`, `global.json`
- `src/EnigmaBudget.Api/EnigmaBudget.Api.csproj`, `Program.cs`, `appsettings.json`
- `src/EnigmaBudget.Api/Data/AppDbContext.cs`
- `src/EnigmaBudget.Api/Data/Migrations/*`
- `src/EnigmaBudget.Api/Identity/CurrentIdentity.cs`
- `src/EnigmaBudget.Api/Onboarding/Contracts.cs`
- `tests/EnigmaBudget.Api.Tests/EnigmaBudget.Api.Tests.csproj`
- `tests/EnigmaBudget.Api.Tests/OnboardingIntegrationTests.cs`, `README.md`

## Self-review and limitations

- Queries for onboarding, accounts and cards derive workspace ownership from the authenticated subject. The client cannot provide a workspace or subject to select another person's data.
- Draft version is an EF concurrency token and updates are persisted with a transaction. Confirmation writes accounts, card debt and separate opening records atomically; database uniqueness and the completed flag prevent duplicate confirmation effects.
- Tests use a test-only identity authentication handler for the two-identity isolation case; it is registered in the integration host only and is not part of the API runtime.
- Google credentials were not available. The unconfigured 503 path is tested; a real external Google authorization/callback exchange was not claimed as tested.
- The slice does not implement household/group access, ledger movements, future installments, S3, or Angular behavior; these remain outside Task 1.
