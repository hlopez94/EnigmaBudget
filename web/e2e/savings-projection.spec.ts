import { expect, test, type Browser, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

type ApiResponse = { status: number; json: () => Promise<any> };

const dateInArgentina = (): string => {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Argentina/Buenos_Aires',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const value = (type: 'year' | 'month' | 'day') =>
    parts.find((part) => part.type === type)?.value ?? '';
  return `${value('year')}-${value('month')}-${value('day')}`;
};

const addDays = (value: string, days: number): string => {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
};

const waitForApi = (page: Page, method: string, path: string | RegExp) =>
  page.waitForResponse((response) => {
    const url = new URL(response.url());
    return (
      (typeof path === 'string' ? url.pathname === path : path.test(url.pathname)) &&
      response.request().method() === method
    );
  });

const signIn = async (page: Page, key: string) => {
  await page.goto('/');
  const localUser = page.getByLabel('Usuario local de prueba');
  if (await localUser.isVisible().catch(() => false)) {
    await localUser.selectOption(key);
    const login = waitForApi(page, 'POST', '/api/auth/development');
    await page.getByRole('button', { name: 'Ingresar como usuario de prueba' }).click();
    expect((await login).status()).toBe(204);
  } else {
    const login = waitForApi(page, 'POST', '/api/auth/development');
    await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
    expect((await login).status()).toBe(204);
  }
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  await expect(page.getByText('Saldo en cuentas', { exact: true })).toBeVisible();
};

const finishEmptyOnboarding = async (page: Page) => {
  const response = await page.request.get('/api/onboarding');
  const state = await response.json();
  if (state.completed) return;
  await page.getByRole('button', { name: 'Abrir configuración' }).click();
  await page.getByRole('button', { name: 'Comenzar' }).click();
  await page.getByRole('button', { name: 'Seguir sin cuentas' }).click();
  await page.getByRole('button', { name: 'Seguir sin tarjetas' }).click();
  await expect(page.getByRole('heading', { name: 'Revisá tu punto de partida' })).toBeVisible();
  const complete = waitForApi(page, 'POST', '/api/onboarding/complete');
  await page.getByRole('button', { name: 'Confirmar configuración' }).click();
  expect((await complete).status()).toBe(200);
};

const openMore = async (page: Page) => {
  const mobile = page.locator('nav.mobile-navigation');
  if (await mobile.isVisible()) {
    await mobile.getByRole('button', { name: 'Más' }).click();
  } else {
    await page.locator('nav.navigation').getByRole('button', { name: 'Más', exact: true }).click();
  }
  await expect(page.locator('.more-screen')).toBeVisible();
};

const openMoreEntry = async (page: Page, label: string) => {
  await openMore(page);
  await page.locator('.more-screen').getByRole('button', { name: new RegExp(label) }).click();
};

const openPrimary = async (page: Page, label: string) => {
  const mobile = page.locator('nav.mobile-navigation');
  if (await mobile.isVisible()) {
    await mobile.getByRole('button', { name: label, exact: true }).click();
  } else {
    await page.locator('nav.navigation').getByRole('button', { name: label, exact: true }).click();
  }
};

const createHousehold = async (page: Page, name: string): Promise<string> => {
  await openMore(page);
  await page.locator('.more-screen').getByRole('button', { name: /Espacios y privacidad/ }).click();
  await page.getByLabel('Nombre del Hogar').fill(name);
  const response = waitForApi(page, 'POST', '/api/workspaces');
  await page.getByRole('button', { name: 'Crear Hogar' }).click();
  const result = await response;
  expect(result.status()).toBe(201);
  const workspace = await result.json();
  await expect(page.locator('.workspace-picker select')).toHaveValue(workspace.id);
  return workspace.id as string;
};

const invite = async (page: Page, email: string, role: 'Member' | 'Reader') => {
  if (!(await page.locator('[name="inviteEmail"]').isVisible().catch(() => false))) {
    await openMore(page);
    await page.locator('.more-screen').getByRole('button', { name: /Espacios y privacidad/ }).click();
  }
  await expect(page.locator('[name="inviteEmail"]')).toBeVisible();
  await page.locator('[name="inviteEmail"]').fill(email);
  await page.locator('[name="inviteRole"]').selectOption(role);
  const response = waitForApi(page, 'POST', /\/api\/workspaces\/[^/]+\/invitations/);
  await page.getByRole('button', { name: 'Crear invitación' }).click();
  const result = await response;
  expect(result.status()).toBe(200);
  return (await result.json()) as { shareUrl: string | null };
};

const acceptInvitation = async (browser: Browser, shareUrl: string, key: string) => {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  page.setDefaultTimeout(10_000);
  await page.goto(shareUrl);
  await page.getByLabel('Usuario local de prueba').selectOption(key);
  const login = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como usuario de prueba' }).click();
  expect((await login).status()).toBe(204);
  await expect(page.getByRole('heading', { name: /Sumarte a/ })).toBeVisible();
  const accepted = waitForApi(page, 'POST', '/api/invitations/accept');
  await page.getByRole('button', { name: 'Aceptar invitación' }).click();
  expect((await accepted).status()).toBe(200);
  await expect(page.locator('.workspace-picker select')).toBeEnabled();
  return { context, page };
};

const createAccount = async (
  page: Page,
  name: string,
  currency: 'ARS' | 'USD',
  balance: string,
  date: string,
) => {
  await openMoreEntry(page, 'Cuentas');
  const add = page.locator('.accounts-list').getByRole('button', { name: /Agregar cuenta/ }).first();
  await add.click();
  const form = page.locator('form').filter({ has: page.locator('[name="accountName"]') });
  await form.locator('[name="accountName"]').fill(name);
  await form.locator('[name="accountBank"]').fill('Banco de prueba');
  await form.locator('[name="accountCurrency"]').selectOption(currency);
  await form.locator('[name="openingBalance"]').fill(balance);
  await form.locator('[name="openingDate"]').fill(date);
  const response = waitForApi(page, 'POST', '/api/accounts');
  await form.getByRole('button', { name: 'Guardar cuenta' }).click();
  expect((await response).status()).toBe(200);
  await expect(page.locator('.accounts-list .resource-card').filter({ hasText: name })).toBeVisible();
};

const createOperation = async (
  page: Page,
  input: {
    kind: 'Income' | 'Expense';
    accountLabel: string;
    date: string;
    description: string;
    amount: string;
    paid: boolean;
  },
) => {
  await openPrimary(page, 'Movimientos');
  await page.getByRole('button', { name: 'Ingreso o gasto' }).click();
  const form = page.locator('form').filter({ has: page.locator('[name="opDescription"]') });
  await form.locator('[name="opKind"]').selectOption(input.kind);
  await form.locator('[name="opAccount"]').selectOption({ label: input.accountLabel });
  await form.locator('[name="opDate"]').fill(input.date);
  await form.locator('[name="opDescription"]').fill(input.description);
  await form.locator('[name="opCategory"]').selectOption({ label: 'Sin clasificar' });
  if (input.kind === 'Income') {
    await form.locator('[name="sourceCreateName"]').fill('Fuente de prueba');
    await form.locator('[name="sourceCreateKind"]').selectOption('Salary');
    await form.getByRole('button', { name: 'Crear fuente y continuar' }).click();
  }
  await form.locator('[name="opAmount"]').fill(input.amount);
  if (input.paid) await form.locator('[name="opPaid"]').check();
  else await form.locator('[name="opPaid"]').uncheck();
  const response = waitForApi(page, 'POST', '/api/operations');
  await form.getByRole('button', { name: 'Guardar movimiento' }).click();
  const result = await response;
  expect(result.status()).toBe(200);
  const created = await result.json();
  expect(created.description).toBe(input.description);
  expect(created.date).toBe(input.date);
  expect(created.amount).toBe(`${input.amount}.00`);
  if (input.paid) await expect(page.locator('.operation-row').filter({ hasText: input.description })).toBeVisible();
  return created;
};

const openGoalDetails = async (goal: ReturnType<Page['locator']>) => {
  const panel = goal.locator('.details-block');
  if (await panel.isVisible().catch(() => false)) return;
  const details = goal.getByRole('button', { name: /Ver asignaciones e historial|Actualizar detalle/ });
  if (await details.isVisible()) {
    await details.click();
    await expect(panel).toBeVisible();
  }
};

const currentCash = async (page: Page, accountName: string) =>
  page.evaluate(async (name) => {
    const workspaceId = document.querySelector<HTMLSelectElement>('.workspace-picker select')?.value;
    if (!workspaceId) throw new Error('No hay un espacio seleccionado.');
    const period = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'America/Argentina/Buenos_Aires',
    }).format(new Date()).slice(0, 7);
    const response = await fetch(`/api/finance?period=${period}`, {
      headers: { 'X-Workspace-Id': workspaceId },
    });
    if (!response.ok) throw new Error(`Consulta financiera: ${response.status}`);
    const overview = await response.json();
    return overview.accounts.find((account: { name: string }) => account.name === name)?.currentBalance as string | undefined;
  }, accountName);

const assertNoHorizontalOverflow = async (page: Page) => {
  const metrics = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth,
    body: document.body.scrollWidth,
  }));
  expect(metrics.document, JSON.stringify(metrics)).toBeLessThanOrEqual(metrics.viewport);
  expect(metrics.body, JSON.stringify(metrics)).toBeLessThanOrEqual(metrics.viewport);
};

const captureAt = async (page: Page, width: number, filename: string) => {
  await page.setViewportSize({ width, height: width === 1280 ? 900 : 844 });
  await assertNoHorizontalOverflow(page);
  await page.screenshot({ path: resolve(process.cwd(), '../work/savings-e2e-shots', filename), fullPage: true });
};

test('real savings, existing payment, projection and scenario journey stays responsive and private', async ({
  browser,
  page,
}) => {
  test.setTimeout(240_000);
  await page.setViewportSize({ width: 390, height: 844 });
  page.setDefaultTimeout(10_000);
  page.setDefaultNavigationTimeout(30_000);
  const browserErrors: string[] = [];
  let expectingAllocationConflict = false;
  let allocationConflictResponseSeen = false;
  let unrelatedConflictSeen = false;
  const expectedConflictConsoleErrors: string[] = [];
  page.on('pageerror', (error) => browserErrors.push(error.message));
  page.on('response', (response) => {
    if (!expectingAllocationConflict || response.status() !== 409) return;
    const request = response.request();
    const pathname = new URL(response.url()).pathname;
    if (request.method() === 'POST' && pathname === '/api/savings/allocations') allocationConflictResponseSeen = true;
    else unrelatedConflictSeen = true;
  });
  page.on('console', (message) => {
    if (message.type() === 'error') {
      if (expectingAllocationConflict && allocationConflictResponseSeen && !unrelatedConflictSeen && /409 \(Conflict\)/.test(message.text())) expectedConflictConsoleErrors.push(message.text());
      else browserErrors.push(message.text());
    }
  });
  const date = dateInArgentina();
  const futureExpenseDate = addDays(date, 3);
  const futureIncomeDate = addDays(date, 8);
  const suffix = randomUUID().slice(0, 6);
  const householdName = `Ahorros QA ${suffix}`;
  const arsAccount = `Cuenta ARS ${suffix}`;
  const usdAccount = `Cuenta USD ${suffix}`;
  const paidDescription = `Compra pagada ${suffix}`;
  const expenseDescription = `Gasto futuro ${suffix}`;
  const incomeDescription = `Cobro futuro ${suffix}`;
  const output = resolve(process.cwd(), '../work/savings-e2e-shots');
  await mkdir(output, { recursive: true });

  await signIn(page, 'alice');
  await expect(page.locator('nav.mobile-navigation button')).toHaveCount(5);
  await expect(page.locator('nav.mobile-navigation').getByRole('button', { name: 'Compartidos' })).toBeVisible();
  await expect(page.locator('nav.mobile-navigation').getByRole('button', { name: 'Cuentas' })).toHaveCount(0);
  await finishEmptyOnboarding(page);
  const workspaceId = await createHousehold(page, householdName);
  const memberInvitation = await invite(page, 'bob@example.test', 'Member');
  const readerInvitation = await invite(page, 'reader@example.test', 'Reader');
  expect(memberInvitation.shareUrl).toBeTruthy();
  expect(readerInvitation.shareUrl).toBeTruthy();
  const member = await acceptInvitation(browser, new URL(memberInvitation.shareUrl!, page.url()).toString(), 'bob');
  const reader = await acceptInvitation(browser, new URL(readerInvitation.shareUrl!, page.url()).toString(), 'reader');

  await createAccount(page, arsAccount, 'ARS', '30000', date);
  await createAccount(page, usdAccount, 'USD', '120', date);
  const accountLabels = [`${arsAccount} · ARS`, `${usdAccount} · USD`];

  await openMoreEntry(page, 'Ahorros');
  await expect(page.getByRole('heading', { name: 'Objetivos de ahorro' })).toBeVisible();
  await page.getByRole('button', { name: 'Nuevo objetivo' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Nuevo objetivo' })).toBeFocused();
  await page.getByRole('button', { name: 'Nuevo objetivo' }).click();
  const goalForm = page.locator('form').filter({ has: page.locator('[name="name"]') });
  const goalName = `Viaje ${suffix}`;
  await goalForm.locator('[name="name"]').fill(goalName);
  await goalForm.locator('[name="currency"]').selectOption('USD');
  await goalForm.locator('[name="amount"]').fill('1000');
  const createGoal = waitForApi(page, 'POST', '/api/savings/goals');
  await goalForm.getByRole('button', { name: 'Guardar objetivo' }).click();
  const createdGoalResponse = await createGoal;
  expect(createdGoalResponse.status()).toBe(200);
  const createdGoal = await createdGoalResponse.json();
  expect(createdGoal.id).toMatch(/^[0-9a-f-]{36}$/i);
  const goal = page.locator('.goal-panel').filter({ hasText: goalName });
  await expect(goal).toBeVisible();
  await expect(goal).toContainText('Configurá un aporte mensual positivo');

  await goal.getByRole('button', { name: 'Elegir cotización' }).click();
  const rateForm = page.locator('form').filter({ has: page.locator('[name="mode"]') });
  await rateForm.locator('[name="mode"]').selectOption('Manual');
  await rateForm.locator('[name="manualRate"]').fill('1250.00');
  await rateForm.locator('[name="reason"]').fill('Presupuesto de viaje de prueba');
  const saveRate = waitForApi(page, 'PUT', /\/api\/savings\/goals\/[^/]+\/rate/);
  await rateForm.getByRole('button', { name: 'Guardar referencia' }).click();
  expect((await saveRate).status()).toBe(200);
  await expect(goal.locator('.rate-provenance')).toContainText('1250');
  await expect(goal.locator('.rate-provenance')).toContainText('Presupuesto de viaje de prueba');

  await goal.getByRole('button', { name: 'Reservar fondos' }).click();
  const allocationForm = page.locator('form').filter({ has: page.locator('[name="account"]') });
  await allocationForm.locator('[name="account"]').selectOption({ label: accountLabels[0] });
  await allocationForm.locator('[name="amount"]').fill('35000');
  expectingAllocationConflict = true;
  allocationConflictResponseSeen = false;
  unrelatedConflictSeen = false;
  const tooMuch = waitForApi(page, 'POST', '/api/savings/allocations');
  await allocationForm.getByRole('button', { name: 'Reservar', exact: true }).click();
  const rejected = await tooMuch;
  await expect.poll(() => expectedConflictConsoleErrors.length, { timeout: 1000 }).toBe(1);
  expectingAllocationConflict = false;
  expect(rejected.status()).toBe(409);
  await expect(allocationForm.locator('[name="amount"]')).toHaveValue('35000');
  await expect(page.getByRole('alert')).toBeVisible();
  await allocationForm.locator('[name="amount"]').fill('28000');

  let delayedReservation = false;
  await page.route('**/api/savings/allocations', async (route) => {
    if (route.request().method() === 'POST' && !delayedReservation) {
      delayedReservation = true;
      await new Promise((resolveDelay) => setTimeout(resolveDelay, 350));
    }
    await route.continue();
  });
  const reserve = waitForApi(page, 'POST', '/api/savings/allocations');
  await allocationForm.getByRole('button', { name: 'Reservar', exact: true }).click();
  await expect(allocationForm.getByRole('button', { name: 'Reservar', exact: true })).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toBeVisible();
  expect((await reserve).status()).toBe(200);
  await page.unroute('**/api/savings/allocations');
  await expect(page.getByRole('dialog')).toBeHidden();
  expect(await currentCash(page, arsAccount)).toBe('30000.00');
  await expect(goal).toContainText(/28\.000/);
  await expect(goal).toContainText('ARS');
  await expect(goal).toContainText(/equivalente estimado/i);
  await openGoalDetails(goal);
  await expect(page.locator('.allocation-row').filter({ hasText: arsAccount })).toBeVisible();

  const createdPaidOperation = await createOperation(page, {
    kind: 'Expense', accountLabel: accountLabels[0], date, description: paidDescription, amount: '5000', paid: true,
  });
  expect(createdPaidOperation.payments).toHaveLength(1);
  const expectedPaymentId = createdPaidOperation.payments[0].id as string;
  expect(await currentCash(page, arsAccount)).toBe('25000.00');
  await openMoreEntry(page, 'Ahorros');
  const selectedGoal = page.locator('.goal-panel').filter({ hasText: goalName });
  await openGoalDetails(selectedGoal);
  await expect(page.locator('.availability-row').filter({ hasText: arsAccount })).toContainText(/Faltante de respaldo/);
  await captureAt(page, 360, 'ahorros-360.png');
  await captureAt(page, 390, 'ahorros-390.png');
  await captureAt(page, 1280, 'ahorros-1280.png');

  const allocationRow = page.locator('.allocation-row').filter({ hasText: arsAccount });
  await allocationRow.getByRole('button', { name: 'Aplicar pago existente' }).click();
  const applyForm = page.locator('form').filter({ has: page.locator('[name="payment"]') });
  const paymentSelect = applyForm.locator('[name="payment"]');
  const matchingPaymentOption = paymentSelect.locator('option').filter({ hasText: paidDescription });
  await expect(matchingPaymentOption).toHaveCount(1);
  await expect(paymentSelect.locator(`option[value="${expectedPaymentId}"]`)).toHaveCount(1);
  const paymentOption = await paymentSelect.locator('option').evaluateAll((items, description) => {
    const found = items.find((item) => item.textContent?.includes(String(description)));
    return found instanceof HTMLOptionElement ? found.value : '';
  }, paidDescription);
  expect(paymentOption).not.toBe('');
  expect(paymentOption).toBe(expectedPaymentId);
  await paymentSelect.selectOption(paymentOption);
  await applyForm.locator('[name="amount"]').fill('5000');
  const applyPayment = waitForApi(page, 'POST', '/api/savings/applications');
  await applyForm.getByRole('button', { name: 'Aplicar pago' }).click();
  expect((await applyPayment).status()).toBe(200);
  await expect(page.getByRole('status')).toContainText(/no se registró un segundo pago/i);
  expect(await currentCash(page, arsAccount)).toBe('25000.00');
  const operationsAfterApplication = await page.evaluate(async (workspaceId) => {
    const response = await fetch('/api/operations', { headers: { 'X-Workspace-Id': workspaceId } });
    return (await response.json()) as { description: string; payments: unknown[] }[];
  }, workspaceId);
  expect(operationsAfterApplication.filter((operation) => operation.description === paidDescription)).toHaveLength(1);
  expect(operationsAfterApplication.find((operation) => operation.description === paidDescription)?.payments).toHaveLength(1);

  const remainingAllocation = page.locator('.allocation-row').filter({ hasText: arsAccount });
  await remainingAllocation.getByRole('button', { name: 'Liberar reserva' }).click();
  const releaseForm = page.locator('form').filter({ has: page.locator('[name="amount"]') });
  const reservedAmount = await releaseForm.locator('[name="amount"]').inputValue();
  const release = waitForApi(page, 'POST', /\/api\/savings\/allocations\/[^/]+\/release/);
  await releaseForm.getByRole('button', { name: 'Liberar reserva' }).click();
  expect((await release).status()).toBe(200);
  expect(Number(reservedAmount)).toBe(23000);
  expect(await currentCash(page, arsAccount)).toBe('25000.00');
  await openGoalDetails(selectedGoal);
  await expect(selectedGoal).toContainText('Todavía no hay reservas para este objetivo.');
  await expect(selectedGoal.locator('.rate-provenance')).toContainText('1250');

  await page.reload();
  await openMoreEntry(page, 'Ahorros');
  await expect(page.locator('.goal-panel').filter({ hasText: goalName })).toBeVisible();
  const persistedSavings = await page.evaluate(async ({ workspaceId, goalId }) => {
    const headers = { 'X-Workspace-Id': workspaceId };
    const goalsResponse = await fetch('/api/savings/goals', { headers });
    const detailsResponse = await fetch(`/api/savings/goals/${goalId}/details?historyPage=1`, { headers });
    return { goals: await goalsResponse.json(), details: await detailsResponse.json() };
  }, { workspaceId, goalId: createdGoal.id });
  expect(persistedSavings.goals.some((item: { id: string }) => item.id === createdGoal.id)).toBe(true);
  expect(persistedSavings.details.allocations[0].reservedAmount).toBe('0.00');
  expect(persistedSavings.details.movements.map((item: { kind: string }) => item.kind).sort()).toEqual(['Allocate', 'Apply', 'Release']);
  expect(persistedSavings.details.movements.find((item: { kind: string }) => item.kind === 'Apply').financePaymentId).toBeTruthy();

  await createOperation(page, {
    kind: 'Expense', accountLabel: accountLabels[0], date: futureExpenseDate,
    description: expenseDescription, amount: '40000', paid: false,
  });
  await createOperation(page, {
    kind: 'Income', accountLabel: accountLabels[0], date: futureIncomeDate,
    description: incomeDescription, amount: '50000', paid: false,
  });

  await openMoreEntry(page, 'Cotizaciones');
  await expect(page.getByRole('heading', { name: 'Referencias de mercado' })).toBeVisible();
  await expect(page.getByText('No hay cotizaciones guardadas.')).toBeVisible();
  await captureAt(page, 360, 'cotizaciones-360.png');
  await captureAt(page, 390, 'cotizaciones-390.png');
  await captureAt(page, 1280, 'cotizaciones-1280.png');
  await openMoreEntry(page, 'Ahorros');
  await expect(page.locator('.goal-panel').filter({ hasText: goalName }).locator('.rate-provenance')).toContainText('1250');

  const confirmedResponse = waitForApi(page, 'GET', '/api/projection');
  await openMoreEntry(page, 'Proyección');
  await expect(page.getByRole('heading', { name: 'Efectivo y compromisos' })).toBeVisible();
  const confirmed = await confirmedResponse;
  expect(confirmed.status()).toBe(200);
  const confirmedData = await confirmed.json();
  const arsProjection = confirmedData.currencies.find((currency: { currency: string }) => currency.currency === 'ARS');
  const usdProjection = confirmedData.currencies.find((currency: { currency: string }) => currency.currency === 'USD');
  expect(arsProjection.cashBalance).toBe('25000.00');
  expect(arsProjection.firstShortfallDate).toBe(futureExpenseDate);
  expect(arsProjection.dailyBalances.at(-1).cashBalance).toBe('-15000.00');
  expect(usdProjection.cashBalance).toBe('120.00');
  expect(usdProjection).toBeTruthy();
  await expect(page.getByText('ARS y USD permanecen separados')).toBeVisible();
  await expect(page.locator('.asof-panel')).toContainText('Confirmado');
  await expect(page.locator('.currency-grid .currency-card')).toHaveCount(2);
  await expect(page.locator('.currency-card').filter({ hasText: 'Pesos argentinos · ARS' })).toContainText('25.000,00');
  await expect(page.locator('.currency-card').filter({ hasText: 'Dólares estadounidenses · USD' })).toContainText('120,00');
  await expect(page.locator('.shortfall')).toContainText(expenseDescription);

  const estimatedBaseResponse = waitForApi(page, 'POST', '/api/projection/calculate');
  await page.locator('[name="certainty"]').selectOption('Estimated');
  expect((await estimatedBaseResponse).status()).toBe(200);
  const assumption = page.getByRole('button', { name: 'Agregar supuesto' });
  await assumption.click();
  const assumptionForm = page.locator('form').filter({ has: page.locator('[name="description"]') });
  await assumptionForm.locator('[name="kind"]').selectOption('Income');
  await assumptionForm.locator('[name="date"]').fill(addDays(date, 5));
  await assumptionForm.locator('[name="currency"]').selectOption('ARS');
  await assumptionForm.locator('[name="amount"]').fill('20000');
  await assumptionForm.locator('[name="description"]').fill('Ingreso estimado de prueba');
  const calculated = waitForApi(page, 'POST', '/api/projection/calculate');
  await assumptionForm.getByRole('button', { name: 'Calcular supuesto' }).click();
  const estimatedResponse = await calculated;
  expect(estimatedResponse.status()).toBe(200);
  const estimatedData = await estimatedResponse.json();
  const estimatedArs = estimatedData.currencies.find((currency: { currency: string }) => currency.currency === 'ARS');
  expect(estimatedArs.firstShortfallDate).toBe(futureExpenseDate);
  expect(Number(estimatedArs.dailyBalances.at(-1).cashBalance)).toBeGreaterThan(0);
  expect(Number(estimatedArs.dailyBalances.at(-1).cashBalance)).toBeGreaterThan(Number(arsProjection.dailyBalances.at(-1).cashBalance));
  await expect(page.getByText('Ingreso estimado de prueba')).toBeVisible();

  await page.getByRole('button', { name: 'Guardar escenario' }).click();
  const scenarioForm = page.locator('form').filter({ has: page.locator('[name="scenarioName"]') });
  const scenarioName = `Mudanza estimada ${suffix}`;
  await scenarioForm.locator('[name="scenarioName"]').fill(scenarioName);
  const scenarioSaved = waitForApi(page, 'POST', '/api/projection/scenarios');
  await scenarioForm.getByRole('button', { name: 'Guardar', exact: true }).click();
  expect((await scenarioSaved).status()).toBe(200);
  await expect(page.locator('[name="scenario"] option', { hasText: scenarioName })).toHaveCount(1);
  await page.reload();
  await openMoreEntry(page, 'Proyección');
  await expect(page.locator('[name="scenario"] option', { hasText: scenarioName })).toHaveCount(1);
  const restoredScenario = waitForApi(page, 'POST', '/api/projection/calculate');
  await page.locator('[name="scenario"]').selectOption({ label: scenarioName });
  expect((await restoredScenario).status()).toBe(200);
  await expect(page.locator('.asof-panel')).toContainText('Escenario estimado');
  await expect(page.getByText('Ingreso estimado de prueba')).toBeVisible();
  await captureAt(page, 360, 'proyeccion-360.png');
  await captureAt(page, 390, 'proyeccion-390.png');
  await captureAt(page, 1280, 'proyeccion-1280.png');

  const memberOptions = await member.page.locator('.workspace-picker select option').evaluateAll((items) => items.map((item) => ({ value: (item as HTMLOptionElement).value, label: item.textContent ?? '' })));
  const memberHousehold = memberOptions.find((item) => item.label.includes(householdName));
  expect(memberHousehold).toBeTruthy();
  await member.page.locator('.workspace-picker select').selectOption({ label: memberHousehold!.label });
  await openMoreEntry(member.page, 'Proyección');
  const memberScenarios = await member.page.locator('[name="scenario"] option').allTextContents();
  expect(memberScenarios.some((label) => label.includes(scenarioName))).toBe(false);
  await expect(member.page.locator('.more-screen')).toBeHidden();
  await openMoreEntry(member.page, 'Ahorros');
  await expect(member.page.locator('.goal-panel').filter({ hasText: goalName })).toBeVisible();

  const readerOptions = await reader.page.locator('.workspace-picker select option').evaluateAll((items) => items.map((item) => ({ value: (item as HTMLOptionElement).value, label: item.textContent ?? '' })));
  const readerHousehold = readerOptions.find((item) => item.label.includes(householdName));
  expect(readerHousehold).toBeTruthy();
  await reader.page.locator('.workspace-picker select').selectOption(readerHousehold!.value);
  await expect(reader.page.locator('nav.mobile-navigation button')).toHaveCount(5);
  const readerNavigation = reader.page.locator('nav.mobile-navigation');
  const canReadGroups = (await readerNavigation.getByRole('button', { name: 'Compartidos' }).count()) === 1;
  if (canReadGroups) {
    await expect(readerNavigation.getByRole('button', { name: 'Compartidos' })).toBeVisible();
    await expect(readerNavigation.getByRole('button', { name: 'Cuentas' })).toHaveCount(0);
  } else {
    await expect(readerNavigation.getByRole('button', { name: 'Cuentas' })).toBeVisible();
    await expect(readerNavigation.getByRole('button', { name: 'Compartidos' })).toHaveCount(0);
  }
  await openMoreEntry(reader.page, 'Ahorros');
  await expect(reader.page.locator('.goal-panel').filter({ hasText: goalName })).toBeVisible();
  await expect(reader.page.getByRole('button', { name: 'Nuevo objetivo' })).toHaveCount(0);
  await expect(reader.page.getByRole('button', { name: 'Reservar fondos' })).toHaveCount(0);
  await openMoreEntry(reader.page, 'Proyección');
  await expect(reader.page.getByRole('button', { name: 'Guardar escenario' })).toHaveCount(0);
  const readerWrite = await reader.page.evaluate(async (workspaceId) => {
    const session = await (await fetch('/api/auth/session')).json();
    const response = await fetch('/api/projection/scenarios', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken, 'X-Workspace-Id': workspaceId },
      body: JSON.stringify({ id: crypto.randomUUID(), name: 'Intento lector', assumptions: [] }),
    });
    return response.status;
  }, readerHousehold!.value);
  expect(readerWrite).toBe(403);

  expect(browserErrors).toEqual([]);
  await member.context.close();
  await reader.context.close();
  expect(workspaceId).toMatch(/^[0-9a-f-]{36}$/i);
});
