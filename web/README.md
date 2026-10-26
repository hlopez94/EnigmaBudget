# EnigmaBudget web

Angular 22 frontend for the EnigmaBudget personal onboarding flow.

## Local development

- `npm ci` installs the locked dependencies.
- `npm start` runs Angular at `http://localhost:4200` and proxies `/api` to `http://localhost:5000` (edit `proxy.conf.json` if the local ASP.NET port differs).
- `npm test -- --run` runs the Vitest suite.
- `npm run build` creates the production files in `dist/web/browser/`.

The ASP.NET host serves the frontend and API from one origin. The integration step should copy the contents of `dist/web/browser/` into `src/EnigmaBudget.Api/wwwroot/`.

`@angular/build` currently resolves Vite through its compatible Vite 8 range. The scoped npm override pins that development/build dependency to Vite 8.3.4, which includes the upstream fixes for the advisories affecting 8.3.0–8.3.2. Keep the override until the Angular build package selects a patched Vite by default.
