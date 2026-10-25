import { defineConfig, devices } from '@playwright/test';
import { resolve } from 'node:path';

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:5080';
const e2eConnection = process.env.ENIGMABUDGET_E2E_CONNECTION;
const dotnet = process.env.DOTNET_COMMAND ?? '../work/tooling/dotnet/dotnet';
const quoteShellArgument = (value: string) => `'${value.replaceAll("'", "'\\''")}'`;
const configDir = process.cwd();
const webRoot = process.env.ENIGMABUDGET_E2E_WEBROOT ?? resolve(configDir, 'dist/web/browser');

if (!e2eConnection) {
  throw new Error(
    'Definí ENIGMABUDGET_E2E_CONNECTION con una base PostgreSQL vacía y descartable.',
  );
}

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: 'list',
  use: {
    baseURL: appUrl,
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome'],
  },
  webServer: process.env.PLAYWRIGHT_BASE_URL
    ? undefined
    : {
        command: `${quoteShellArgument(dotnet)} run --project ../src/EnigmaBudget.Api/EnigmaBudget.Api.csproj --no-launch-profile --urls http://127.0.0.1:5080`,
        url: `${appUrl}/api/health`,
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
        env: {
          ...process.env,
          ASPNETCORE_ENVIRONMENT: 'Development',
          DOTNET_PROCESSOR_COUNT: process.env.DOTNET_PROCESSOR_COUNT ?? '1',
          DOTNET_CLI_USE_MSBUILD_SERVER: '0',
          MSBUILDDISABLENODEREUSE: '1',
          UseSharedCompilation: 'false',
          Authentication__DevelopmentLoginEnabled: 'true',
          Authentication__DevelopmentUsers__Users__0__Key: 'alice',
          Authentication__DevelopmentUsers__Users__0__Subject: 'e2e-alice',
          Authentication__DevelopmentUsers__Users__0__DisplayName: 'Ana de prueba',
          Authentication__DevelopmentUsers__Users__0__Email: 'alice@example.test',
          Authentication__DevelopmentUsers__Users__1__Key: 'bob',
          Authentication__DevelopmentUsers__Users__1__Subject: 'e2e-bob',
          Authentication__DevelopmentUsers__Users__1__DisplayName: 'Bruno de prueba',
          Authentication__DevelopmentUsers__Users__1__Email: 'bob@example.test',
          Authentication__DevelopmentUsers__Users__2__Key: 'reader',
          Authentication__DevelopmentUsers__Users__2__Subject: 'e2e-reader',
          Authentication__DevelopmentUsers__Users__2__DisplayName: 'Rita de prueba',
          Authentication__DevelopmentUsers__Users__2__Email: 'reader@example.test',
          Authentication__DevelopmentUsers__Users__3__Key: 'dana',
          Authentication__DevelopmentUsers__Users__3__Subject: 'e2e-dana',
          Authentication__DevelopmentUsers__Users__3__DisplayName: 'Dana de prueba',
          Authentication__DevelopmentUsers__Users__3__Email: 'dana@example.test',
          ConnectionStrings__Default: e2eConnection ?? '',
          ExchangeRates__AutomaticRefreshEnabled: 'false',
          ASPNETCORE_WEBROOT: webRoot,
        },
      },
});
