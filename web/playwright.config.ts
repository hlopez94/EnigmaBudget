import { defineConfig, devices } from '@playwright/test';

const appUrl = process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:5080';
const e2eConnection = process.env.ENIGMABUDGET_E2E_CONNECTION;
const dotnet = process.env.DOTNET_COMMAND ?? '../work/tooling/dotnet/dotnet';
const quoteShellArgument = (value: string) => `'${value.replaceAll("'", "'\\''")}'`;

if (!e2eConnection) {
  throw new Error('Definí ENIGMABUDGET_E2E_CONNECTION con una base PostgreSQL vacía y descartable.');
}

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  retries: 0,
  reporter: 'list',
  use: {
    baseURL: appUrl,
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome'],
  },
  webServer: process.env.PLAYWRIGHT_BASE_URL ? undefined : {
    command: `${quoteShellArgument(dotnet)} run --project ../src/EnigmaBudget.Api/EnigmaBudget.Api.csproj --no-launch-profile --urls http://127.0.0.1:5080`,
    url: `${appUrl}/api/health`,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      Authentication__DevelopmentLoginEnabled: 'true',
      ConnectionStrings__Default: e2eConnection ?? '',
    },
  },
});
