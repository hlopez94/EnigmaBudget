import { expect, test, type Page } from '@playwright/test';
import { resolve } from 'node:path';

const waitForApi = (page: Page, method: string, path: string | RegExp) =>
  page.waitForResponse((response) => {
    const url = new URL(response.url());
    return (typeof path === 'string' ? url.pathname === path : path.test(url.pathname)) && response.request().method() === method;
  });

const signInAs = async (page: Page, key: string) => {
  await page.goto('/');
  await page.getByLabel('Usuario local de prueba').selectOption(key);
  const login = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como usuario de prueba' }).click();
  expect((await login).status()).toBe(204);
  await expect(page.getByRole('heading', { name: 'Tu panorama financiero' })).toBeVisible();
};

const openMore = async (page: Page) => page.locator('nav.mobile-navigation').getByRole('button', { name: 'Más' }).click();
const openSpaces = async (page: Page) => {
  if (await page.locator('.create-space').isVisible().catch(() => false)) return;
  if (await page.locator('.more-screen').isVisible().catch(() => false)) {
    await page.locator('.more-screen').getByRole('button', { name: /Hogares, integrantes y privacidad/ }).click();
  } else if (!(await page.getByRole('heading', { name: 'Espacios y privacidad' }).isVisible().catch(() => false))) {
    await openMore(page);
    const discardDialog = page.getByRole('dialog');
    if (await discardDialog.isVisible().catch(() => false)) await discardDialog.getByRole('button', { name: /Descartar y cambiar/ }).click();
    await expect(page.locator('.more-screen')).toBeVisible();
    await page.locator('.more-screen').getByRole('button', { name: /Hogares, integrantes y privacidad/ }).click();
  }
  await expect(page.locator('.create-space')).toBeVisible();
};

const createAccount = async (page: Page, name: string, balance: string) => {
  await openMore(page);
  await page.locator('.more-screen').getByRole('button', { name: /Cuentas/ }).click();
  await page.locator('.accounts-list').getByRole('button', { name: /Agregar cuenta/ }).first().click();
  const form = page.locator('form').filter({ has: page.locator('[name="accountName"]') });
  await form.locator('[name="accountName"]').fill(name);
  await form.locator('[name="accountBank"]').fill('Banco de prueba');
  await form.locator('[name="accountCurrency"]').selectOption('ARS');
  await form.locator('[name="openingBalance"]').fill(balance);
  await form.locator('[name="openingDate"]').fill(new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Argentina/Buenos_Aires' }).format(new Date()));
  const saved = waitForApi(page, 'POST', '/api/accounts');
  await form.getByRole('button', { name: 'Guardar cuenta' }).click();
  expect((await saved).status()).toBe(200);
};

const invite = async (page: Page, email: string, role: 'Member' | 'Reader') => {
  await page.locator('[name="inviteEmail"]').fill(email);
  await page.locator('[name="inviteRole"]').selectOption(role);
  const pending = waitForApi(page, 'POST', /\/api\/workspaces\/[^/]+\/invitations/);
  await page.getByRole('button', { name: 'Crear invitación' }).click();
  const response = await pending;
  expect(response.status()).toBe(200);
  return (await response.json()) as { shareUrl: string | null };
};

test('real Alice, Bob and Reader contexts enforce Household scope, personal consent, capacity and revocation', async ({
  browser,
  page: alice,
}) => {
  test.setTimeout(120_000);
  await alice.setViewportSize({ width: 390, height: 844 });
  await signInAs(alice, 'alice');
  const now = new Date();
  const date = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Argentina/Buenos_Aires',
  }).format(now);
  const month = date.slice(0, 7);

  await createAccount(alice, 'Cuenta personal de Ana', '5000');
  await openSpaces(alice);
  await alice.getByLabel('Nombre del Hogar').fill('Casa de Ana');
  const created = waitForApi(alice, 'POST', '/api/workspaces');
  await alice.getByRole('button', { name: 'Crear Hogar' }).click();
  const householdResponse = await created;
  expect(householdResponse.status()).toBe(201);
  const household = (await householdResponse.json()) as { id: string };
  await expect(alice.locator('.workspace-picker select')).toHaveValue(household.id);

  const bobContext = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const bob = await bobContext.newPage();
  await openSpaces(alice);
  const bobInvitation = await invite(alice, 'bob@example.test', 'Member');
  expect(bobInvitation.shareUrl).toBeTruthy();
  await bob.goto(new URL(bobInvitation.shareUrl!, alice.url()).toString());
  await bob.getByLabel('Usuario local de prueba').selectOption('bob');
  const bobLogin = waitForApi(bob, 'POST', '/api/auth/development');
  await bob.getByRole('button', { name: 'Ingresar como usuario de prueba' }).click();
  expect((await bobLogin).status()).toBe(204);
  await expect(bob.getByRole('heading', { name: /Sumarte a Casa de Ana/ })).toBeVisible();
  const bobAccepted = waitForApi(bob, 'POST', '/api/invitations/accept');
  await bob.getByRole('button', { name: 'Aceptar invitación' }).click();
  expect((await bobAccepted).status()).toBe(200);
  await expect(bob.locator('.workspace-picker select')).toHaveValue(household.id);

  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Mi espacio Personal' });
  await openSpaces(alice);
  await expect(alice.getByRole('button', { name: 'Compartir con integrantes actuales' })).toBeVisible();
  const consent = waitForApi(alice, 'PUT', `/api/personal-sharing/${household.id}`);
  await alice.getByRole('button', { name: 'Compartir con integrantes actuales' }).click();
  expect((await consent).status()).toBe(200);

  await bob.reload();
  await openSpaces(bob);
  await expect(bob.locator('.workspace-picker select option', { hasText: 'Personal de Ana de prueba · Solo lectura' })).toHaveCount(1);
  await bob.getByLabel('Cambiar espacio').first().selectOption({ label: 'Personal de Ana de prueba · Solo lectura' });
  await openSpaces(bob);
  await expect(bob.locator('.space-control strong')).toContainText('Personal de Ana de prueba · Solo lectura');
  const sharedPersonal = await bob.evaluate(async () => {
    const selector = document.querySelector<HTMLSelectElement>('.workspace-picker select')!;
    const csrf = (await (await fetch('/api/auth/session')).json()).csrfToken as string;
    const workspaceId = selector.value;
    const response = await fetch('/api/finance?period=2026-10', { headers: { 'X-Workspace-Id': workspaceId } });
    const groups = await fetch('/api/groups', { headers: { 'X-Workspace-Id': workspaceId } });
    return { workspaceId, financeStatus: response.status, finance: await response.json(), groupsStatus: groups.status, csrf };
  });
  expect(sharedPersonal.financeStatus).toBe(200);
  expect(sharedPersonal.finance.accounts.some((account: { name: string }) => account.name === 'Cuenta personal de Ana')).toBe(true);
  expect(sharedPersonal.groupsStatus).toBe(403);
  const writeDenied = await bob.evaluate(async ({ workspaceId, csrf }) => {
    const accountsResponse = await fetch('/api/accounts', { headers: { 'X-Workspace-Id': workspaceId } });
    const account = (await accountsResponse.json())[0];
    const response = await fetch(`/api/accounts/${account.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf, 'X-Workspace-Id': workspaceId },
      body: JSON.stringify({ version: account.version, name: 'No autorizado', bankName: account.bankName, currency: account.currency, isArchived: false }),
    });
    return response.status;
  }, { workspaceId: sharedPersonal.workspaceId, csrf: sharedPersonal.csrf });
  expect(writeDenied).toBe(403);

  await alice.getByLabel('Nombre del Hogar').fill('Borrador de Hogar');
  await alice.getByRole('button', { name: 'Más', exact: true }).click();
  await expect(alice.locator('.more-screen')).toBeVisible();
  await expect(alice.locator('.space-control')).toBeHidden();
  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Titular' });
  await expect(alice.getByRole('dialog')).toBeVisible();
  await alice.getByRole('button', { name: 'Seguir acá' }).click();
  await openSpaces(alice);
  await expect(alice.getByLabel('Nombre del Hogar')).toHaveValue('Borrador de Hogar');
  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Titular' });
  await alice.getByRole('button', { name: /Descartar y cambiar/ }).click();
  await expect(alice.getByRole('dialog')).toHaveCount(0);
  await expect(alice.locator('.space-control strong')).toContainText('Casa de Ana · Titular');
  await expect(alice.locator('.workspace-picker select')).toHaveValue(household.id);
  await openSpaces(alice);
  await expect(alice.getByLabel('Nombre del Hogar')).toHaveValue('');

  await createAccount(alice, 'Cuenta compartida', '1000');
  await alice.getByRole('button', { name: 'Movimientos', exact: true }).click();
  await alice.getByRole('button', { name: /Ingreso o gasto/ }).click();
  const expenseForm = alice.locator('form').filter({ has: alice.locator('[name="opDescription"]') });
  await expenseForm.locator('[name="opKind"]').selectOption('Expense');
  await expenseForm.locator('[name="opAccount"]').selectOption({ label: 'Cuenta compartida · ARS' });
  await expenseForm.locator('[name="opDate"]').fill(date);
  await expenseForm.locator('[name="opDescription"]').fill('Compra del Hogar');
  await expenseForm.locator('[name="opCategory"]').fill('Comida');
  await expenseForm.locator('[name="opAmount"]').fill('125.50');
  await expenseForm.locator('[name="opPaid"]').check();
  const expense = waitForApi(alice, 'POST', '/api/operations');
  await expenseForm.getByRole('button', { name: 'Guardar movimiento' }).click();
  expect((await expense).status()).toBe(200);
  const aliceBalances = await alice.evaluate(async (period) => {
    const workspaceId = document.querySelector<HTMLSelectElement>('.workspace-picker select')!.value;
    return fetch(`/api/finance?period=${period}`, { headers: { 'X-Workspace-Id': workspaceId } }).then(response => response.json());
  }, month);
  expect(aliceBalances.accounts.find((account: { name: string }) => account.name === 'Cuenta compartida').currentBalance).toBe('874.50');
  await bob.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Integrante' });
  const bobHousehold = await bob.evaluate(async ({ householdId, period }) => {
    const response = await fetch('/api/operations', { headers: { 'X-Workspace-Id': householdId } });
    return { status: response.status, rows: await response.json(), period };
  }, { householdId: household.id, period: month });
  expect(bobHousehold.status).toBe(200);
  expect(bobHousehold.rows.some((operation: { description: string }) => operation.description === 'Compra del Hogar')).toBe(true);
  await bob.getByLabel('Cambiar espacio').first().selectOption({ label: 'Personal de Ana de prueba · Solo lectura' });
  await openSpaces(bob);
  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Mi espacio Personal' });
  const personalAfterExpense = await alice.evaluate(async (period) => {
    const workspaceId = document.querySelector<HTMLSelectElement>('.workspace-picker select')!.value;
    return fetch(`/api/finance?period=${period}`, { headers: { 'X-Workspace-Id': workspaceId } }).then(response => response.json());
  }, month);
  expect(personalAfterExpense.accounts.find((account: { name: string }) => account.name === 'Cuenta personal de Ana').currentBalance).toBe('5000.00');

  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Titular' });
  await openSpaces(alice);
  const readerContext = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const reader = await readerContext.newPage();
  const readerInvitation = await invite(alice, 'reader@example.test', 'Reader');
  expect(readerInvitation.shareUrl).toBeTruthy();
  let releaseInvite!: () => void;
  let signalIntercept!: () => void;
  let intercepted = false;
  const requestIntercepted = new Promise<void>(resolve => { signalIntercept = resolve; });
  const continueGate = new Promise<void>(resolve => { releaseInvite = resolve; });
  await alice.route('**/api/workspaces/*/invitations', async route => {
    if (!intercepted && route.request().method() === 'POST' && route.request().postData()?.includes('dana@example.test')) {
      intercepted = true;
      signalIntercept();
      await continueGate;
    }
    await route.continue();
  });
  await alice.locator('[name="inviteEmail"]').fill('dana@example.test');
  await alice.locator('[name="inviteRole"]').selectOption('Member');
  const danaRequest = waitForApi(alice, 'POST', /\/api\/workspaces\/[^/]+\/invitations/);
  await alice.getByRole('button', { name: 'Crear invitación' }).click();
  await requestIntercepted;
  await expect(alice.locator('.workspace-picker select')).toBeDisabled();
  await expect(alice.getByRole('button', { name: 'Crear invitación' })).toBeDisabled();
  releaseInvite();
  const danaResponse = await danaRequest;
  expect(danaResponse.status()).toBe(200);
  expect((await danaResponse.json()).shareUrl).toBeTruthy();
  await alice.unroute('**/api/workspaces/*/invitations');
  await expect(alice.getByRole('button', { name: 'Crear invitación' })).toBeDisabled();
  await reader.goto(new URL(readerInvitation.shareUrl!, alice.url()).toString());
  await reader.getByLabel('Usuario local de prueba').selectOption('reader');
  const readerLogin = waitForApi(reader, 'POST', '/api/auth/development');
  await reader.getByRole('button', { name: 'Ingresar como usuario de prueba' }).click();
  expect((await readerLogin).status()).toBe(204);
  const readerAccepted = waitForApi(reader, 'POST', '/api/invitations/accept');
  await reader.getByRole('button', { name: 'Aceptar invitación' }).click();
  expect((await readerAccepted).status()).toBe(200);
  const readerWriteAttempt = await reader.evaluate(async ({ householdId, period, date }) => {
    const workspaceHeaders = { 'X-Workspace-Id': householdId };
    const beforeFinanceResponse = await fetch(`/api/finance?period=${period}`, { headers: workspaceHeaders });
    const beforeOperationsResponse = await fetch('/api/operations', { headers: workspaceHeaders });
    const beforeFinance = await beforeFinanceResponse.json();
    const beforeOperations = await beforeOperationsResponse.json();
    const account = beforeFinance.accounts[0];
    const session = await (await fetch('/api/auth/session')).json();
    const denied = await fetch('/api/operations', {
      method: 'POST',
      headers: { ...workspaceHeaders, 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken },
      body: JSON.stringify({ id: crypto.randomUUID(), kind: 'Expense', accountId: account.id, date, description: 'Intento de Reader bloqueado', category: 'Prueba', amount: '50.00', paid: false }),
    });
    const afterFinance = await (await fetch(`/api/finance?period=${period}`, { headers: workspaceHeaders })).json();
    const afterOperations = await (await fetch('/api/operations', { headers: workspaceHeaders })).json();
    return {
      financeReadStatus: beforeFinanceResponse.status,
      ledgerReadStatus: beforeOperationsResponse.status,
      writeStatus: denied.status,
      beforeBalance: account.currentBalance,
      afterBalance: afterFinance.accounts.find((item: { id: string }) => item.id === account.id)?.currentBalance,
      beforeOperations: beforeOperations.length,
      afterOperations: afterOperations.length,
      rejectedDescriptionPresent: afterOperations.some((item: { description: string }) => item.description === 'Intento de Reader bloqueado'),
    };
  }, { householdId: household.id, period: month, date });
  expect(readerWriteAttempt.financeReadStatus).toBe(200);
  expect(readerWriteAttempt.ledgerReadStatus).toBe(200);
  expect(readerWriteAttempt.writeStatus).toBe(403);
  expect(readerWriteAttempt.afterBalance).toBe(readerWriteAttempt.beforeBalance);
  expect(readerWriteAttempt.afterOperations).toBe(readerWriteAttempt.beforeOperations);
  expect(readerWriteAttempt.rejectedDescriptionPresent).toBe(false);

  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Mi espacio Personal' });
  await openSpaces(alice);
  await expect(alice.getByRole('button', { name: 'Renovar alcance' })).toBeVisible();
  const revoked = waitForApi(alice, 'PUT', `/api/personal-sharing/${household.id}`);
  await alice.getByRole('button', { name: 'Revocar acceso' }).click();
  expect((await revoked).status()).toBe(200);
  const revokedRead = await bob.evaluate(async (workspaceId) => fetch('/api/finance?period=2026-10', { headers: { 'X-Workspace-Id': workspaceId } }).then(response => response.status), sharedPersonal.workspaceId);
  expect(revokedRead).toBe(404);
  await expect(bob.locator('.space-control strong')).toContainText('Personal de Ana de prueba · Solo lectura');
  const refreshedWorkspaces = waitForApi(bob, 'GET', '/api/workspaces');
  await bob.reload();
  expect((await refreshedWorkspaces).status()).toBe(200);
  await expect(bob.locator('.workspace-picker select')).not.toHaveValue(sharedPersonal.workspaceId);
  await expect(bob.getByText('Cuenta personal de Ana')).toHaveCount(0);

  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Titular' });
  await openSpaces(reader);
  await reader.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Solo lectura' });
  await expect(reader.getByRole('button', { name: 'Salir del Hogar' })).toBeVisible();
  reader.on('dialog', dialog => dialog.accept());
  const left = waitForApi(reader, 'POST', `/api/workspaces/${household.id}/leave`);
  await reader.getByRole('button', { name: 'Salir del Hogar' }).click();
  expect((await left).status()).toBe(204);
  const privateAfterLeave = await reader.evaluate(async (workspaceId) => fetch('/api/finance?period=2026-10', { headers: { 'X-Workspace-Id': workspaceId } }).then(response => response.status), sharedPersonal.workspaceId);
  expect(privateAfterLeave).toBe(404);

  await openSpaces(alice);
  const rejoinInvitation = await invite(alice, 'reader@example.test', 'Reader');
  expect(rejoinInvitation.shareUrl).toBeTruthy();
  await reader.goto(new URL(rejoinInvitation.shareUrl!, alice.url()).toString());
  await expect(reader.getByRole('heading', { name: /Sumarte a Casa de Ana/ })).toBeVisible();
  const reaccepted = waitForApi(reader, 'POST', '/api/invitations/accept');
  await reader.getByRole('button', { name: 'Aceptar invitación' }).click();
  expect((await reaccepted).status()).toBe(200);
  const stillPrivateAfterRejoin = await reader.evaluate(async (workspaceId) => fetch('/api/finance?period=2026-10', { headers: { 'X-Workspace-Id': workspaceId } }).then(response => response.status), sharedPersonal.workspaceId);
  expect(stillPrivateAfterRejoin).toBe(404);

  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Mi espacio Personal' });
  await openSpaces(alice);
  await expect(alice.getByRole('button', { name: 'Compartir con integrantes actuales' })).toBeVisible();
  const restoredConsent = waitForApi(alice, 'PUT', `/api/personal-sharing/${household.id}`);
  await alice.getByRole('button', { name: 'Compartir con integrantes actuales' }).click();
  expect((await restoredConsent).status()).toBe(200);
  const readerPersonalRestored = await reader.evaluate(async (workspaceId) => fetch('/api/finance?period=2026-10', { headers: { 'X-Workspace-Id': workspaceId } }).then(response => response.status), sharedPersonal.workspaceId);
  expect(readerPersonalRestored).toBe(200);

  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Titular' });
  await openSpaces(alice);
  alice.on('dialog', dialog => dialog.accept());
  const archived = waitForApi(alice, 'PUT', `/api/workspaces/${household.id}`);
  await alice.getByRole('button', { name: 'Archivar Hogar' }).click();
  expect((await archived).status()).toBe(200);
  const archivedWrite = await alice.evaluate(async ({ householdId, period }) => {
    const account = (await (await fetch('/api/accounts', { headers: { 'X-Workspace-Id': householdId } })).json())[0];
    const session = await (await fetch('/api/auth/session')).json();
    const response = await fetch('/api/operations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken, 'X-Workspace-Id': householdId },
      body: JSON.stringify({ id: crypto.randomUUID(), kind: 'Expense', accountId: account.id, date: period + '-25', description: 'Movimiento bloqueado', category: 'Prueba', amount: '1.00', paid: false }),
    });
    return response.status;
  }, { householdId: household.id, period: month });
  expect(archivedWrite).toBe(403);

  await alice.getByLabel('Cambiar espacio').first().selectOption({ label: 'Mi espacio Personal' });
  await openSpaces(alice);
  await expect(alice.getByRole('button', { name: 'Revocar acceso' })).toBeVisible();
  const archivedRevocation = waitForApi(alice, 'PUT', `/api/personal-sharing/${household.id}`);
  await alice.getByRole('button', { name: 'Revocar acceso' }).click();
  expect((await archivedRevocation).status()).toBe(200);
  const revokedArchivedReader = await reader.evaluate(async (workspaceId) => fetch('/api/finance?period=2026-10', { headers: { 'X-Workspace-Id': workspaceId } }).then(response => response.status), sharedPersonal.workspaceId);
  expect(revokedArchivedReader).toBe(404);

  await openSpaces(reader);
  await expect(reader.getByRole('button', { name: 'Salir del Hogar' })).toBeVisible();
  const archivedLeave = waitForApi(reader, 'POST', `/api/workspaces/${household.id}/leave`);
  await reader.getByRole('button', { name: 'Salir del Hogar' }).click();
  expect((await archivedLeave).status()).toBe(204);
  await bob.getByLabel('Cambiar espacio').first().selectOption({ label: 'Casa de Ana · Integrante' });
  await openSpaces(bob);
  await expect(bob.getByRole('button', { name: 'Salir del Hogar' })).toBeVisible();
  bob.on('dialog', dialog => dialog.accept());
  const bobLeave = waitForApi(bob, 'POST', `/api/workspaces/${household.id}/leave`);
  await bob.getByRole('button', { name: 'Salir del Hogar' }).click();
  expect((await bobLeave).status()).toBe(204);

  const ownerLeave = await alice.evaluate(async (workspaceId) => {
    const session = await (await fetch('/api/auth/session')).json();
    const workspace = await (await fetch(`/api/workspaces/${workspaceId}`)).json();
    const response = await fetch(`/api/workspaces/${workspaceId}/leave`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken }, body: JSON.stringify({ version: workspace.membershipVersion }) });
    return response.status;
  }, household.id);
  expect(ownerLeave).toBe(403);

  await alice.screenshot({ path: resolve(process.cwd(), '../work/task4-workspaces-mobile-390.png'), fullPage: true });

  await bobContext.close();
  await readerContext.close();
});

test('mobile workspace header stays readable and in bounds at 360 and 390 px', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await signInAs(page, 'alice');
  await openMore(page);
  await expect(page.locator('.more-screen')).toBeVisible();
  await page.locator('.more-screen').getByRole('button', { name: /Hogares, integrantes y privacidad/ }).click();
  await expect(page.locator('.create-space')).toBeVisible();
  const title = page.getByRole('heading', { name: 'Espacios y privacidad' });
  await expect(title).toBeVisible();

  for (const width of [360, 390]) {
    await page.setViewportSize({ width, height: 844 });
    const geometry = await page.locator('.workspace-heading').evaluate((header) => {
      const box = (element: Element) => {
        const rect = element.getBoundingClientRect();
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height };
      };
      const titleBlock = header.querySelector<HTMLElement>(':scope > div:first-child')!;
      const workspace = header.querySelector<HTMLSelectElement>('.workspace-picker select')!;
      const period = header.querySelector<HTMLInputElement>('.period-picker input')!;
      return {
        viewport: window.innerWidth,
        documentWidth: document.documentElement.scrollWidth,
        titleBlock: box(titleBlock),
        title: box(titleBlock.querySelector('h1')!),
        workspace: box(workspace),
        workspaceFont: Number.parseFloat(getComputedStyle(workspace).fontSize),
        period: box(period),
      };
    });
    expect(geometry.documentWidth, JSON.stringify(geometry)).toBeLessThanOrEqual(width);
    expect(geometry.titleBlock.width, JSON.stringify(geometry)).toBeGreaterThanOrEqual(width - 48);
    expect(geometry.title.width, JSON.stringify(geometry)).toBeGreaterThan(0);
    expect(geometry.titleBlock.bottom, JSON.stringify(geometry)).toBeLessThanOrEqual(geometry.workspace.top);
    expect(geometry.workspace.bottom, JSON.stringify(geometry)).toBeLessThanOrEqual(geometry.period.top);
    expect(geometry.workspace.height, JSON.stringify(geometry)).toBeGreaterThanOrEqual(44);
    expect(geometry.workspace.width, JSON.stringify(geometry)).toBeGreaterThanOrEqual(width - 48);
    expect(geometry.workspaceFont, JSON.stringify(geometry)).toBeGreaterThanOrEqual(16);
    expect(geometry.period.height, JSON.stringify(geometry)).toBeGreaterThanOrEqual(44);
    expect(geometry.period.width, JSON.stringify(geometry)).toBeGreaterThanOrEqual(140);
    for (const control of [geometry.titleBlock, geometry.workspace, geometry.period]) {
      expect(control.left, JSON.stringify(geometry)).toBeGreaterThanOrEqual(0);
      expect(control.right, JSON.stringify(geometry)).toBeLessThanOrEqual(width);
    }
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: resolve(process.cwd(), '../work/task4-workspaces-mobile-390.png'), fullPage: true });
});
