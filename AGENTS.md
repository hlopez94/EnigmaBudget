# EnigmaBudget

- Angular 22 + OptimusUI 2 (the release compatible with Angular 22), .NET 10, PostgreSQL.
- One ASP.NET host serves the API and compiled Angular frontend. No Ionic or microservices.
- All subagents use gpt-6-luna as explicitly requested by the user.
- No private spreadsheet links, financial data, credentials, or tokens in this public repository.
- Read docs/superpowers/specs/2026-10-24-finanzas-design.md and the task brief provided by the controller.
- Use Spanish user-facing copy. Amounts are decimal strings at API boundaries, stored as PostgreSQL numeric.
- Enforce membership server-side; never trust a tenant ID or identity supplied by the client.
- Development sign-in is explicitly opt-in and forbidden outside Development. Google sign-in must fail closed when unconfigured.
- Implementers do not spawn agents. Only the controller commits integration decisions and dispatches reviews.
- Tooling may be available at work/tooling/dotnet/dotnet. PostgreSQL local port: 55432.
