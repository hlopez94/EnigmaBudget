import { expect, test, type Page } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

const dateInArgentina = (): string =>
  (() => {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'America/Argentina/Buenos_Aires',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).formatToParts(new Date());
    const value = (type: 'year' | 'month' | 'day') =>
      parts.find((part) => part.type === type)?.value ?? '';
    return `${value('year')}-${value('month')}-${value('day')}`;
  })();

const monthAfter = (period: string): string => {
  const [year, month] = period.split('-').map(Number);
  return month === 12 ? `${year + 1}-01` : `${year}-${String(month + 1).padStart(2, '0')}`;
};

const waitForApi = (page: Page, method: string, path: string) =>
  page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === path && response.request().method() === method;
  });

test('saves and reloads an onboarding draft, then confirms an empty setup', async ({ page }) => {
  const date = dateInArgentina();
  await page.goto('/');
  const signIn = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
  expect((await signIn).status()).toBe(204);
  await expect(page.getByRole('heading', { name: /Tu panorama financiero/ })).toBeVisible();
  await page.getByRole('button', { name: 'Abrir configuración' }).click();
  await page.getByRole('button', { name: 'Comenzar' }).click();
  await page.getByRole('button', { name: 'Agregar cuenta' }).click();
  await page.getByLabel('Nombre de la cuenta').fill('Apertura de prueba');
  await page.getByLabel('Banco o entidad').fill('Banco de prueba');
  await page.getByLabel('Saldo inicial').fill('12345');
  await page.getByLabel('Fecha del saldo').fill(date);
  const savedAccountDraft = waitForApi(page, 'PUT', '/api/onboarding');
  await page.getByRole('button', { name: 'Guardar avance' }).click();
  expect((await savedAccountDraft).status()).toBe(200);
  await page.getByRole('button', { name: 'Guardar y continuar' }).click();
  await page.getByRole('button', { name: 'Agregar tarjeta' }).click();
  await page.getByLabel('Nombre de la tarjeta').fill('Tarjeta de prueba');
  await page.getByLabel('Banco o entidad').fill('Banco de prueba');
  await page.getByLabel('Deuda actual en ARS').fill('3210');
  await page.getByLabel('Deuda actual en USD').fill('0');
  await page.getByLabel('Fecha del resumen').fill(date);
  const savedCardDraft = waitForApi(page, 'PUT', '/api/onboarding');
  await page.getByRole('button', { name: 'Guardar avance' }).click();
  expect((await savedCardDraft).status()).toBe(200);

  await page.reload();
  await page.getByRole('button', { name: 'Abrir configuración' }).click();
  await page.getByRole('button', { name: 'Comenzar' }).click();
  await expect(page.getByLabel('Nombre de la cuenta')).toHaveValue('Apertura de prueba');
  await page.getByRole('button', { name: 'Guardar y continuar' }).click();
  await expect(page.getByLabel('Nombre de la tarjeta')).toHaveValue('Tarjeta de prueba');
  await expect(page.getByLabel('Deuda actual en ARS')).toHaveValue('3210');
  await page.getByRole('button', { name: 'Guardar y continuar' }).click();
  await expect(page.getByRole('heading', { name: 'Revisá tu punto de partida' })).toBeVisible();

  await page.getByRole('button', { name: 'Editar cuentas' }).click();
  await page.getByRole('button', { name: 'Quitar cuenta Apertura de prueba' }).click();
  const clearedAccounts = waitForApi(page, 'PUT', '/api/onboarding');
  await page.getByRole('button', { name: 'Seguir sin cuentas' }).click();
  expect((await clearedAccounts).status()).toBe(200);
  await page.getByRole('button', { name: 'Quitar tarjeta Tarjeta de prueba' }).click();
  const clearedCards = waitForApi(page, 'PUT', '/api/onboarding');
  await page.getByRole('button', { name: 'Seguir sin tarjetas' }).click();
  expect((await clearedCards).status()).toBe(200);
  const completion = waitForApi(page, 'POST', '/api/onboarding/complete');
  await page.getByRole('button', { name: 'Confirmar configuración' }).click();
  expect((await completion).status()).toBe(200);
  const state = await (await page.request.get('/api/onboarding')).json();
  expect(state).toMatchObject({ completed: true, accounts: [], cards: [] });
});

test('runs real finance, sharing, and card acceptance from empty completed onboarding', async ({
  page,
}) => {
  const date = dateInArgentina();
  const month = date.slice(0, 7);
  const nextMonth = monthAfter(month);

  await page.goto('/');
  const signIn = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
  expect((await signIn).status()).toBe(204);
  await expect(page.getByRole('heading', { name: /Tu panorama financiero/ })).toBeVisible();
  await expect(page.getByText('Empezá por agregar una cuenta')).toBeVisible();

  await page.getByRole('button', { name: 'Cuentas', exact: true }).click();
  const accountForm = page.locator('form').filter({ has: page.locator('[name="accountName"]') });
  const createPrimary = waitForApi(page, 'POST', '/api/accounts');
  await accountForm.locator('[name="accountName"]').fill('Cuenta principal');
  await accountForm.locator('[name="accountBank"]').fill('Banco Uno');
  await accountForm.locator('[name="openingBalance"]').fill('100000');
  await accountForm.locator('[name="openingDate"]').fill(date);
  await accountForm.getByRole('button', { name: 'Guardar cuenta' }).click();
  expect((await createPrimary).status()).toBe(200);

  const createSecondary = waitForApi(page, 'POST', '/api/accounts');
  await accountForm.locator('[name="accountName"]').fill('Cuenta secundaria');
  await accountForm.locator('[name="accountBank"]').fill('Banco Dos');
  await accountForm.locator('[name="openingBalance"]').fill('0');
  await accountForm.locator('[name="openingDate"]').fill(date);
  await accountForm.getByRole('button', { name: 'Guardar cuenta' }).click();
  expect((await createSecondary).status()).toBe(200);

  await page.getByRole('button', { name: 'Tarjetas y resúmenes' }).click();
  const cardForm = page.locator('form').filter({ has: page.locator('[name="cardName"]') });
  const createCard = waitForApi(page, 'POST', '/api/cards');
  await cardForm.locator('[name="cardName"]').fill('Visa familiar');
  await cardForm.locator('[name="cardBank"]').fill('Banco Uno');
  await cardForm.locator('[name="cardAsOf"]').fill(date);
  await cardForm.getByRole('button', { name: 'Guardar tarjeta' }).click();
  expect((await createCard).status()).toBe(200);

  await page.getByRole('button', { name: 'Movimientos', exact: true }).click();
  const operationForm = page
    .locator('form')
    .filter({ has: page.locator('[name="opDescription"]') });
  const createIncome = waitForApi(page, 'POST', '/api/operations');
  await operationForm.locator('[name="opKind"]').selectOption('Income');
  await operationForm
    .locator('[name="opAccount"]')
    .selectOption({ label: 'Cuenta principal · ARS' });
  await operationForm.locator('[name="opDate"]').fill(date);
  await operationForm.locator('[name="opDescription"]').fill('Ingreso de prueba');
  await operationForm.locator('[name="opCategory"]').fill('Trabajo');
  await operationForm.locator('[name="opAmount"]').fill('1000');
  await operationForm.locator('[name="opPaid"]').check();
  await operationForm.getByRole('button', { name: 'Guardar movimiento' }).click();
  expect((await createIncome).status()).toBe(200);

  const createExpense = waitForApi(page, 'POST', '/api/operations');
  await operationForm.locator('[name="opKind"]').selectOption('Expense');
  await operationForm
    .locator('[name="opAccount"]')
    .selectOption({ label: 'Cuenta principal · ARS' });
  await operationForm.locator('[name="opDate"]').fill(date);
  await operationForm.locator('[name="opDescription"]').fill('Gasto de prueba');
  await operationForm.locator('[name="opCategory"]').fill('Comida');
  await operationForm.locator('[name="opAmount"]').fill('100');
  await operationForm.locator('[name="opPaid"]').check();
  await operationForm.getByRole('button', { name: 'Guardar movimiento' }).click();
  expect((await createExpense).status()).toBe(200);

  const transferForm = page
    .locator('form')
    .filter({ has: page.locator('[name="transferAmount"]') });
  const transfer = waitForApi(page, 'POST', '/api/transfers');
  await transferForm
    .locator('[name="fromAccount"]')
    .selectOption({ label: 'Cuenta principal · ARS' });
  await transferForm
    .locator('[name="toAccount"]')
    .selectOption({ label: 'Cuenta secundaria · ARS' });
  await transferForm.locator('[name="transferDate"]').fill(date);
  await transferForm.locator('[name="transferAmount"]').fill('100');
  await transferForm.getByRole('button', { name: 'Registrar transferencia' }).click();
  expect((await transfer).status()).toBe(200);

  let overview = await (await page.request.get(`/api/finance?period=${month}`)).json();
  expect(
    overview.accounts.find((account: any) => account.name === 'Cuenta principal').currentBalance,
  ).toBe('100800.00');
  expect(
    overview.accounts.find((account: any) => account.name === 'Cuenta secundaria').currentBalance,
  ).toBe('100.00');

  await page.getByRole('button', { name: 'Compartidos', exact: true }).click();
  const groupForm = page.locator('form').filter({ has: page.locator('[name="groupName"]') });
  const createGroup = waitForApi(page, 'POST', '/api/groups');
  await groupForm.locator('[name="groupName"]').fill('Casa compartida');
  await groupForm.locator('[name="selfName"]').fill('Yo');
  await groupForm.locator('[name="otherNames"]').fill('Luz');
  await groupForm.getByRole('button', { name: 'Crear grupo' }).click();
  expect((await createGroup).status()).toBe(200);

  overview = await (await page.request.get(`/api/finance?period=${month}`)).json();
  const group = overview.groups.find((item: any) => item.name === 'Casa compartida');
  expect(group).toBeTruthy();
  const groupId = group.id as string;
  const ruleForm = page.locator('form').filter({ has: page.locator('[name="ruleMode"]') });
  const saveFirstRule = waitForApi(page, 'PUT', `/api/groups/${groupId}/rules/${month}`);
  await ruleForm.locator('[name="ruleMode"]').selectOption('Percentage');
  await ruleForm.getByLabel('Yo (vos)').fill('60');
  await ruleForm.getByLabel('Luz').fill('40');
  await ruleForm.getByRole('button', { name: 'Guardar regla mensual' }).click();
  expect((await saveFirstRule).status()).toBe(200);
  const confirmFirstRule = waitForApi(
    page,
    'POST',
    `/api/groups/${groupId}/rules/${month}/confirm`,
  );
  await page.getByRole('button', { name: 'Confirmar reparto del mes' }).click();
  expect((await confirmFirstRule).status()).toBe(200);

  await page.getByRole('button', { name: 'Tarjetas y resúmenes' }).click();
  const cardResource = page.locator('.resource-card').filter({ hasText: 'Visa familiar' });
  await cardResource.getByRole('button', { name: 'Ver resumen y compras' }).click();
  const purchaseForm = page
    .locator('form')
    .filter({ has: page.locator('[name="purchaseDescription"]') });
  const purchase = waitForApi(page, 'POST', '/api/purchases');
  await purchaseForm.locator('[name="purchaseDescription"]').fill('Compra familiar');
  await purchaseForm.locator('[name="purchaseCategory"]').fill('Hogar');
  await purchaseForm.locator('[name="purchaseDate"]').fill(date);
  await purchaseForm.locator('[name="purchaseCurrency"]').selectOption('ARS');
  await purchaseForm.locator('[name="purchaseTotal"]').fill('120000');
  await purchaseForm.locator('[name="installmentCount"]').fill('3');
  await purchaseForm.locator('[name="firstPeriod"]').fill(month);
  await purchaseForm.locator('[name="purchaseGroup"]').selectOption({ label: 'Casa compartida' });
  await purchaseForm.getByRole('button', { name: 'Guardar compra' }).click();
  expect((await purchase).status()).toBe(200);

  const card = overview.cards.find((item: any) => item.name === 'Visa familiar');
  const cardId = card.id as string;
  const purchases = await (await page.request.get(`/api/cards/${cardId}/purchases`)).json();
  expect(purchases).toHaveLength(1);
  expect(purchases[0].installments.map((item: any) => item.amount)).toEqual([
    '40000.00',
    '40000.00',
    '40000.00',
  ]);

  await page.getByRole('button', { name: 'Compartidos', exact: true }).click();
  let receivables = await (
    await page.request.get(`/api/groups/${groupId}/receivables?period=${month}`)
  ).json();
  let guestShare = receivables.find((item: any) => item.participantName === 'Luz');
  expect(guestShare).toMatchObject({ amount: '16000.00', pendingAmount: '16000.00' });
  const shareRow = page
    .locator('.operation-row')
    .filter({ hasText: 'Compra familiar' })
    .filter({ hasText: 'Luz' });

  for (let collectionNumber = 0; collectionNumber < 2; collectionNumber++) {
    await shareRow.getByRole('button', { name: 'Cobrar parte' }).click();
    const collectionForm = shareRow.locator('form.inline-payment');
    const collect = waitForApi(page, 'POST', `/api/groups/${groupId}/collections`);
    await collectionForm.locator('[name="collectionAccount"]').selectOption({
      label: 'Cuenta principal · ARS',
    });
    await collectionForm.locator('[name="collectionAmount"]').fill('8000');
    await collectionForm.locator('[name="appliedCollection"]').fill('8000');
    await collectionForm.locator('[name="collectionDate"]').fill(date);
    await collectionForm.getByRole('button', { name: 'Registrar cobro' }).click();
    const collectionResponse = await collect;
    expect(collectionResponse.status()).toBe(200);
    if (collectionNumber === 0) {
      const csrfToken = await page.evaluate(async () => {
        const session = await fetch('/api/auth/session');
        return ((await session.json()) as { csrfToken: string }).csrfToken;
      });
      const retry = await page.request.post(`/api/groups/${groupId}/collections`, {
        data: collectionResponse.request().postDataJSON(),
        headers: { 'X-CSRF-TOKEN': csrfToken },
      });
      expect(retry.status()).toBe(200);
    }
    receivables = await (
      await page.request.get(`/api/groups/${groupId}/receivables?period=${month}`)
    ).json();
    guestShare = receivables.find((item: any) => item.participantName === 'Luz');
    expect(guestShare.collectedAmount).toBe(`${(collectionNumber + 1) * 8000}.00`);
    expect(guestShare.pendingAmount).toBe(collectionNumber === 0 ? '8000.00' : '0.00');
  }
  await expect(shareRow.getByRole('button', { name: 'Cobrar parte' })).toHaveCount(0);

  await page.getByRole('button', { name: 'Tarjetas y resúmenes' }).click();
  await page.locator('input[aria-label="Período a consultar"]').fill(month);
  await cardResource.getByRole('button', { name: 'Ver resumen y compras' }).click();
  let statement = await (
    await page.request.get(`/api/cards/${cardId}/statement?period=${month}&currency=ARS`)
  ).json();
  expect(statement.expectedAmount).toBe('40000.00');
  const statementForm = page.locator('form').filter({ has: page.locator('[name="actualTotal"]') });
  await statementForm.locator('[name="actualTotal"]').fill('40000');
  const saveStatement = waitForApi(page, 'PUT', `/api/cards/${cardId}/statement`);
  await statementForm.getByRole('button', { name: 'Guardar revisión' }).click();
  expect((await saveStatement).status()).toBe(200);
  const confirmStatement = waitForApi(page, 'POST', `/api/cards/${cardId}/statement/confirm`);
  await statementForm.getByRole('button', { name: 'Confirmar resumen' }).click();
  expect((await confirmStatement).status()).toBe(200);
  await expect(page.locator('form.pay-card')).toBeVisible();

  for (let paymentNumber = 0; paymentNumber < 2; paymentNumber++) {
    const paymentForm = page.locator('form.pay-card');
    const payment = waitForApi(page, 'POST', `/api/cards/${cardId}/payments`);
    await paymentForm.locator('[name="cardPayAccount"]').selectOption({
      label: 'Cuenta principal · ARS',
    });
    await paymentForm.locator('[name="cardPayDate"]').fill(date);
    await paymentForm.locator('[name="cardPayAmount"]').fill('20000');
    await paymentForm.locator('[name="cardPayApplied"]').fill('20000');
    await paymentForm.getByRole('button', { name: 'Registrar pago' }).click();
    expect((await payment).status()).toBe(200);
  }
  statement = await (
    await page.request.get(`/api/cards/${cardId}/statement?period=${month}&currency=ARS`)
  ).json();
  expect(statement.pendingAmount).toBe('0.00');
  expect(statement.payments).toHaveLength(2);

  await page.getByRole('button', { name: 'Compartidos', exact: true }).click();
  const period = page.locator('input[aria-label="Período a consultar"]');
  const nextOverview = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === '/api/finance' && url.searchParams.get('period') === nextMonth;
  });
  await period.fill(nextMonth);
  expect((await nextOverview).status()).toBe(200);
  overview = await (await page.request.get(`/api/finance?period=${nextMonth}`)).json();
  const futureRule = page.locator('form').filter({ has: page.locator('[name="ruleMode"]') });
  const saveFutureRule = waitForApi(page, 'PUT', `/api/groups/${groupId}/rules/${nextMonth}`);
  await futureRule.locator('[name="ruleMode"]').selectOption('Percentage');
  await futureRule.getByLabel('Yo (vos)').fill('30');
  await futureRule.getByLabel('Luz').fill('70');
  await futureRule.getByRole('button', { name: 'Guardar regla mensual' }).click();
  expect((await saveFutureRule).status()).toBe(200);
  await expect(page.getByText('Regla guardada · versión 1')).toBeVisible();
  await expect(futureRule.getByLabel('Yo (vos)')).toHaveValue('30.00');
  await expect(futureRule.getByLabel('Luz')).toHaveValue('70.00');
  let future = await (
    await page.request.get(`/api/groups/${groupId}/receivables?period=${nextMonth}`)
  ).json();
  let futureShare = future.find((item: any) => item.participantName === 'Luz');
  expect(futureShare).toMatchObject({ amount: '28000.00', isEstimated: false });
  expect(futureShare.amount).toBe('28000.00');

  const updateFutureRule = waitForApi(page, 'PUT', `/api/groups/${groupId}/rules/${nextMonth}`);
  await futureRule.getByLabel('Yo (vos)').fill('50');
  await futureRule.getByLabel('Luz').fill('50');
  await expect(futureRule.getByLabel('Yo (vos)')).toHaveValue('50');
  await expect(futureRule.getByLabel('Luz')).toHaveValue('50');
  await futureRule.getByRole('button', { name: 'Guardar regla mensual' }).click();
  expect((await updateFutureRule).status()).toBe(200);
  future = await (
    await page.request.get(`/api/groups/${groupId}/receivables?period=${nextMonth}`)
  ).json();
  futureShare = future.find((item: any) => item.participantName === 'Luz');
  expect(futureShare.amount).toBe('20000.00');

  const currentOverview = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === '/api/finance' && url.searchParams.get('period') === month;
  });
  await period.fill(month);
  expect((await currentOverview).status()).toBe(200);
  const immutableRule = page.locator('form').filter({ has: page.locator('[name="ruleMode"]') });
  await expect(immutableRule.getByRole('button', { name: 'Guardar regla mensual' })).toBeDisabled();
  await expect(
    page.getByText('Reparto confirmado e inmutable para', { exact: false }),
  ).toBeVisible();
  receivables = await (
    await page.request.get(`/api/groups/${groupId}/receivables?period=${month}`)
  ).json();
  guestShare = receivables.find((item: any) => item.participantName === 'Luz');
  expect(guestShare).toMatchObject({ amount: '16000.00', collectedAmount: '16000.00' });

  overview = await (await page.request.get(`/api/finance?period=${month}`)).json();
  expect(
    overview.accounts.find((account: any) => account.name === 'Cuenta principal').currentBalance,
  ).toBe('76800.00');
  expect(
    overview.accounts.find((account: any) => account.name === 'Cuenta secundaria').currentBalance,
  ).toBe('100.00');
  expect(overview.totals.cardDebtArs).toBe('80000.00');

  await page.getByRole('button', { name: 'Inicio', exact: true }).click();
  await page.reload();
  await expect(page.locator('.balance-card.ars strong')).toContainText('76.900');
  const accountsPanel = page.locator('.dashboard-columns .dash-panel').first();
  await expect(
    accountsPanel.locator('.list-row').filter({ hasText: 'Cuenta principal' }).locator('strong'),
  ).toHaveText(/\$\s*76\.800,00/);
  await expect(
    accountsPanel.locator('.list-row').filter({ hasText: 'Cuenta secundaria' }).locator('strong'),
  ).toHaveText(/\$\s*100,00/);
  await expect(page.locator('.summary-card.debt').first()).toContainText('80.000');
  const output = resolve(process.cwd(), '../work');
  await mkdir(output, { recursive: true });
  await page.screenshot({ path: resolve(output, 'task3-finance-desktop.png'), fullPage: true });

  await page.setViewportSize({ width: 360, height: 800 });
  await page.reload();
  await expect(page.getByRole('heading', { name: /Tu panorama financiero/ })).toBeVisible();
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth,
    navViewport: document.querySelector('nav.navigation')?.clientWidth ?? 0,
    navContent: document.querySelector('nav.navigation')?.scrollWidth ?? 0,
  }));
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport);
  expect(dimensions.navContent).toBeGreaterThan(dimensions.navViewport);
  await page.screenshot({ path: resolve(output, 'task3-finance-mobile-360.png'), fullPage: true });
  const sharingNavigation = page.getByRole('button', { name: 'Compartidos', exact: true });
  await sharingNavigation.scrollIntoViewIfNeeded();
  await sharingNavigation.click();
  await expect(page.getByRole('heading', { name: 'Repartos y cobros mensuales' })).toBeVisible();
  await expect(page.locator('.balance-card.ars')).toHaveCount(0);
  const sharingWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(sharingWidth).toBeLessThanOrEqual(360);
  await page.screenshot({
    path: resolve(output, 'task3-sharing-mobile-360.png'),
    fullPage: true,
  });
});
