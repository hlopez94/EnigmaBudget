import { expect, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

test('saves a bilingual account and card draft, confirms once, then keeps separate balances', async ({ page }) => {
  await page.goto('/');
  const unknownApi = await page.request.get('/api/no-existe');
  expect(unknownApi.status()).toBe(404);
  expect(unknownApi.headers()['content-type'] ?? '').not.toContain('text/html');
  await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
  await page.getByRole('button', { name: 'Comenzar' }).click();

  await page.getByRole('button', { name: 'Agregar cuenta' }).click();
  const accounts = page.locator('.resource-card');
  await accounts.nth(0).getByLabel('Nombre de la cuenta').fill('Caja ARS');
  await accounts.nth(0).getByLabel('Banco o entidad').fill('Banco Uno');
  await accounts.nth(0).getByLabel('Saldo inicial').fill('12345,67');
  await accounts.nth(0).getByLabel('Fecha del saldo').fill('2026-10-20');

  await page.getByRole('button', { name: 'Agregar cuenta' }).click();
  await accounts.nth(1).getByLabel('Nombre de la cuenta').fill('Ahorro USD');
  await accounts.nth(1).getByLabel('Banco o entidad').fill('Banco Dos');
  await accounts.nth(1).getByLabel('Saldo inicial').fill('987,65');
  await accounts.nth(1).getByLabel('Fecha del saldo').fill('2026-10-21');
  await accounts.nth(1).getByRole('combobox').click();
  await page.getByText('Dólares · USD', { exact: true }).click();
  await page.getByRole('button', { name: 'Guardar y continuar' }).click();

  await page.getByRole('button', { name: 'Agregar tarjeta' }).click();
  const card = page.locator('.resource-card');
  await card.getByLabel('Nombre de la tarjeta').fill('Visa personal');
  await card.getByLabel('Banco o entidad').fill('Banco Uno');
  await card.getByLabel('Deuda actual en ARS').fill('3210,50');
  await card.getByLabel('Deuda actual en USD').fill('12,34');
  await card.getByLabel('Fecha del resumen').fill('2026-10-22');

  const savedCardDraft = page.waitForResponse(response => response.url().endsWith('/api/onboarding') && response.request().method() === 'PUT');
  await page.getByRole('button', { name: 'Guardar avance' }).click();
  expect((await savedCardDraft).status()).toBe(200);
  await expect(page.getByText('Visa personal')).toBeVisible();
  await page.reload();

  await page.getByRole('button', { name: 'Comenzar' }).click();
  await expect(page.getByLabel('Nombre de la cuenta').nth(0)).toHaveValue('Caja ARS');
  await expect(page.getByLabel('Nombre de la cuenta').nth(1)).toHaveValue('Ahorro USD');
  await page.getByRole('button', { name: 'Guardar y continuar' }).click();
  await expect(page.getByLabel('Nombre de la tarjeta')).toHaveValue('Visa personal');
  await page.getByRole('button', { name: 'Guardar y continuar' }).click();
  await expect(page.getByRole('heading', { name: 'Revisá tu punto de partida' })).toBeVisible();

  await page.getByRole('button', { name: 'Confirmar configuración' }).click();
  await expect(page.getByRole('heading', { name: /Hola,/ })).toBeVisible();
  await page.reload();

  await expect(page.locator('.balance-card.ars strong')).toContainText('12.345,67');
  await expect(page.locator('.balance-card.usd strong')).toContainText('987,65');
  await expect(page.locator('.dashboard-columns .dash-panel').nth(0).getByText('Caja ARS')).toHaveCount(1);
  await expect(page.locator('.dashboard-columns .dash-panel').nth(0).getByText('Ahorro USD')).toHaveCount(1);
  await expect(page.locator('.dashboard-columns .dash-panel').nth(1).getByText('Visa personal')).toHaveCount(1);
  await expect(page.locator('.dashboard-columns .dash-panel').nth(1)).toContainText('3.210,50');
  await expect(page.locator('.dashboard-columns .dash-panel').nth(1)).toContainText('12,34');

  const output = resolve(process.cwd(), '../work');
  await mkdir(output, { recursive: true });
  await page.screenshot({ path: resolve(output, 'task3-dashboard.png'), fullPage: true });
});
