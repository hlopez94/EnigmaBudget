import { expect, test, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
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

const mobileNavigationButton = (page: Page, name: string) =>
  page.locator('nav.mobile-navigation').getByRole('button', { name, exact: true });

test('saves and reloads an onboarding draft, then confirms an empty setup', async ({ page }) => {
  const date = dateInArgentina();
  await page.goto('/');
  const signIn = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
  expect((await signIn).status()).toBe(204);
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  await expect(page.getByText('Saldo en cuentas', { exact: true })).toBeVisible();
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

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  const signIn = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
  expect((await signIn).status()).toBe(204);
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  await expect(page.getByText('Saldo en cuentas', { exact: true })).toBeVisible();
  await expect(page.getByText('Empezá por agregar una cuenta')).toBeVisible();

  await page.getByRole('button', { name: 'Más', exact: true }).click();
  await page.locator('.more-screen').getByRole('button', { name: /Cuentas/ }).click();
  const addAccount = page
    .locator('.accounts-list')
    .getByRole('button', { name: /Agregar cuenta/ })
    .first();
  await addAccount.click();
  const accountForm = page.locator('form').filter({ has: page.locator('[name="accountName"]') });
  const accountDialog = page.getByRole('dialog');
  const closeAccountDialog = accountDialog.getByRole('button', { name: 'Cerrar', exact: true });
  await expect
    .poll(() =>
      accountDialog.evaluate((dialog) =>
        new DOMMatrixReadOnly(getComputedStyle(dialog).transform).a,
      ),
    )
    .toBeGreaterThan(0.99999);
  const accountDialogMetrics = await accountDialog.evaluate((dialog) => ({
    fields: Array.from(dialog.querySelectorAll('input:not([type="checkbox"]), select, textarea')).map(
      (field) => ({
        height: field.getBoundingClientRect().height,
        fontSize: Number.parseFloat(getComputedStyle(field).fontSize),
      }),
    ),
    buttons: Array.from(dialog.querySelectorAll('button')).map((button) => ({
      className: button.className,
      parentClass: button.parentElement?.className,
      minHeight: getComputedStyle(button).minHeight,
      width: button.getBoundingClientRect().width,
      height: button.getBoundingClientRect().height,
    })),
    transform: getComputedStyle(dialog).transform,
  }));
  expect(
    accountDialogMetrics.fields.every((field) => field.height >= 44 && field.fontSize >= 16),
    JSON.stringify(accountDialogMetrics),
  ).toBe(true);
  expect(
    accountDialogMetrics.buttons.every((button) => button.height >= 44),
    JSON.stringify(accountDialogMetrics),
  ).toBe(true);
  expect(
    accountDialogMetrics.buttons.every((button) => button.width >= 44),
    JSON.stringify(accountDialogMetrics),
  ).toBe(true);
  await expect(closeAccountDialog).toBeVisible();
  const dialogTheme = await accountDialog.evaluate((dialog) => {
    const header = dialog.querySelector('.p-dialog-header')!;
    const title = header.querySelector('.p-dialog-title')!;
    const rgb = (value: string) => value.match(/[\d.]+/g)!.slice(0, 3).map(Number);
    const luminance = (value: string) =>
      rgb(value)
        .map((channel) => {
          const normalized = channel / 255;
          return normalized <= 0.04045
            ? normalized / 12.92
            : ((normalized + 0.055) / 1.055) ** 2.4;
        })
        .reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0);
    const headerBackground = getComputedStyle(header).backgroundColor;
    const headerColor = getComputedStyle(title).color;
    const [lighter, darker] = [luminance(headerBackground), luminance(headerColor)].sort(
      (left, right) => right - left,
    );
    return {
      rootBackground: getComputedStyle(dialog).backgroundColor,
      headerBackground,
      headerColor,
      headerContrast: (lighter + 0.05) / (darker + 0.05),
      borderColor: getComputedStyle(dialog).borderTopColor,
      shadow: getComputedStyle(dialog).boxShadow,
      closeLabel: dialog.querySelector('button.p-dialog-close-button')?.getAttribute('aria-label'),
    };
  });
  expect(dialogTheme.rootBackground).toBe('rgb(255, 255, 255)');
  expect(dialogTheme.headerBackground).toBe('rgb(248, 250, 247)');
  expect(dialogTheme.headerContrast).toBeGreaterThanOrEqual(7);
  expect(dialogTheme.borderColor).toBe('rgb(223, 230, 221)');
  expect(dialogTheme.shadow).not.toBe('none');
  expect(dialogTheme.closeLabel).toBe('Cerrar');
  const output = resolve(process.cwd(), '../work');
  await mkdir(output, { recursive: true });
  await page.screenshot({ path: resolve(output, 'task2-account-dialog-mobile-390.png') });
  await accountForm.locator('[name="accountName"]').fill('Borrador móvil');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(addAccount).toBeFocused();
  await page.getByRole('button', { name: 'Más', exact: true }).click();
  await page.locator('.more-screen').getByRole('button', { name: /Cuentas/ }).click();
  await addAccount.click();
  await expect(accountForm.locator('[name="accountName"]')).toHaveValue('Borrador móvil');
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Más', exact: true }).click();
  await page.locator('.more-screen').getByRole('button', { name: /Cuentas/ }).click();
  await addAccount.click();
  const createPrimary = waitForApi(page, 'POST', '/api/accounts');
  await accountForm.locator('[name="accountName"]').fill('Cuenta principal');
  await accountForm.locator('[name="accountBank"]').fill('Banco Uno');
  await accountForm.locator('[name="openingBalance"]').fill('100000');
  await accountForm.locator('[name="openingDate"]').fill(date);
  let savingAccount = false;
  await page.route('**/api/accounts', async (route) => {
    if (route.request().method() === 'POST' && !savingAccount) {
      savingAccount = true;
      await new Promise((resolve) => setTimeout(resolve, 250));
    }
    await route.continue();
  });
  await accountForm.getByRole('button', { name: 'Guardar cuenta' }).click();
  await expect(accountForm.getByRole('button', { name: 'Guardando…' })).toBeDisabled();
  expect((await createPrimary).status()).toBe(200);
  await page.unroute('**/api/accounts');

  await expect(
    page.locator('.accounts-list .resource-card').filter({ hasText: 'Cuenta principal' }),
  ).toBeVisible();
  await addAccount.click();
  const createSecondary = waitForApi(page, 'POST', '/api/accounts');
  await accountForm.locator('[name="accountName"]').fill('Cuenta secundaria');
  await accountForm.locator('[name="accountBank"]').fill('Banco Dos');
  await accountForm.locator('[name="openingBalance"]').fill('0');
  await accountForm.locator('[name="openingDate"]').fill(date);
  await accountForm.getByRole('button', { name: 'Guardar cuenta' }).click();
  expect((await createSecondary).status()).toBe(200);

  await page.getByRole('button', { name: 'Tarjetas', exact: true }).click();
  await page.getByRole('button', { name: 'Agregar tarjeta' }).first().click();
  const cardForm = page.locator('form').filter({ has: page.locator('[name="cardName"]') });
  const createCard = waitForApi(page, 'POST', '/api/cards');
  await cardForm.locator('[name="cardName"]').fill('Visa familiar');
  await cardForm.locator('[name="cardBank"]').fill('Banco Uno');
  await cardForm.locator('[name="cardAsOf"]').fill(date);
  await cardForm.getByRole('button', { name: 'Guardar tarjeta' }).click();
  expect((await createCard).status()).toBe(200);
  await page.getByRole('button', { name: 'Agregar tarjeta' }).first().click();
  const createSecondCard = waitForApi(page, 'POST', '/api/cards');
  await cardForm.locator('[name="cardName"]').fill('Visa secundaria');
  await cardForm.locator('[name="cardBank"]').fill('Banco Dos');
  await cardForm.locator('[name="cardAsOf"]').fill(date);
  await cardForm.getByRole('button', { name: 'Guardar tarjeta' }).click();
  expect((await createSecondCard).status()).toBe(200);

  await mobileNavigationButton(page, 'Movimientos').click();
  await page.getByRole('button', { name: 'Ingreso o gasto' }).click();
  const operationForm = page
    .locator('form')
    .filter({ has: page.locator('[name="opDescription"]') });
  const rejectedOperation = waitForApi(page, 'POST', '/api/operations');
  await operationForm.locator('[name="opKind"]').selectOption('Income');
  await operationForm
    .locator('[name="opAccount"]')
    .selectOption({ label: 'Cuenta principal · ARS' });
  const rejectedAccountValue = await operationForm.locator('[name="opAccount"]').inputValue();
  await operationForm.locator('[name="opDate"]').fill(date);
  await operationForm.locator('[name="opDescription"]').fill('Ingreso de prueba');
  await operationForm.locator('[name="opCategory"]').selectOption({ label: 'Sin clasificar' });
  await operationForm.locator('[name="sourceCreateName"]').fill('Ingreso E2E');
  await operationForm.locator('[name="sourceCreateKind"]').selectOption('Salary');
  await operationForm.getByRole('button', { name: 'Crear fuente y continuar' }).click();
  await operationForm.locator('[name="opAmount"]').fill('0');
  await operationForm.locator('[name="opPaid"]').check();
  await operationForm.getByRole('button', { name: 'Guardar movimiento' }).click();
  expect((await rejectedOperation).status()).toBe(400);
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(operationForm.locator('[name="opKind"]')).toHaveValue('Income');
  await expect(operationForm.locator('[name="opAccount"]')).toHaveValue(rejectedAccountValue);
  await expect(operationForm.locator('[name="opDate"]')).toHaveValue(date);
  await expect(operationForm.locator('[name="opDescription"]')).toHaveValue('Ingreso de prueba');
  await expect(operationForm.locator('[name="opCategory"]')).not.toHaveValue('');
  await expect(operationForm.locator('[name="opIncomeSource"]')).not.toHaveValue('');
  await expect(operationForm.locator('[name="opAmount"]')).toHaveValue('0');
  await expect(operationForm.locator('[name="opPaid"]')).toBeChecked();

  const createIncome = waitForApi(page, 'POST', '/api/operations');
  await operationForm.locator('[name="opAmount"]').fill('1000');
  await operationForm.getByRole('button', { name: 'Guardar movimiento' }).click();
  expect((await createIncome).status()).toBe(200);

  await page.getByRole('button', { name: 'Ingreso o gasto' }).click();
  const createExpense = waitForApi(page, 'POST', '/api/operations');
  await operationForm.locator('[name="opKind"]').selectOption('Expense');
  await operationForm
    .locator('[name="opAccount"]')
    .selectOption({ label: 'Cuenta principal · ARS' });
  await operationForm.locator('[name="opDate"]').fill(date);
  await operationForm.locator('[name="opDescription"]').fill('Gasto de prueba');
  await operationForm.locator('[name="opCategory"]').selectOption({ label: 'Sin clasificar' });
  await operationForm.locator('[name="opAmount"]').fill('100');
  await operationForm.locator('[name="opPaid"]').check();
  await operationForm.getByRole('button', { name: 'Guardar movimiento' }).click();
  expect((await createExpense).status()).toBe(200);

  await page.getByRole('button', { name: 'Transferir' }).click();
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

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.locator('nav.navigation').getByRole('button', { name: 'Inicio', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  const periodInput = page.getByLabel('Período a consultar');
  const quotaCaption = page.locator('.finance-row-label small').first();
  const appliedCaption = await quotaCaption.innerText();
  const financePeriods: string[] = [];
  await page.route('**/api/finance**', async (route) => {
    financePeriods.push(new URL(route.request().url()).searchParams.get('period') ?? '');
    await route.continue();
  });

  await periodInput.fill('');
  const periodError = page.getByRole('alert');
  await expect(periodError).toContainText(/período válido/i);
  await expect(quotaCaption).toHaveText(appliedCaption);
  await page.getByRole('button', { name: 'Actualizar', exact: true }).click();
  await expect(periodError).toContainText(/período válido/i);
  await page.waitForTimeout(150);
  expect(financePeriods).toEqual([]);

  const reloadedFinance = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === '/api/finance' && url.searchParams.get('period') === month;
  });
  await periodInput.fill(month);
  expect((await reloadedFinance).status()).toBe(200);
  overview = await (await page.request.get(`/api/finance?period=${month}`)).json();
  expect(
    overview.accounts.find((account: any) => account.name === 'Cuenta principal').currentBalance,
  ).toBe('100800.00');
  expect(
    overview.accounts.find((account: any) => account.name === 'Cuenta secundaria').currentBalance,
  ).toBe('100.00');
  expect(financePeriods).toEqual([month]);
  await page.unroute('**/api/finance**');
  await page.setViewportSize({ width: 390, height: 844 });

  await mobileNavigationButton(page, 'Compartidos').click();
  await page.getByRole('button', { name: 'Crear grupo' }).first().click();
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

  await mobileNavigationButton(page, 'Tarjetas').click();
  const cardResource = page.locator('.resource-card').filter({ hasText: 'Visa familiar' });
  await cardResource.getByRole('button', { name: 'Ver resumen y compras' }).click();
  const purchaseForm = page
    .locator('form')
    .filter({ has: page.locator('[name="purchaseDescription"]') });
  await page.setViewportSize({ width: 360, height: 800 });
  await page.getByRole('button', { name: 'Cargar compra' }).click();
  const lastPurchaseField = purchaseForm.locator('[name="purchaseGroup"]');
  const firstPurchaseField = purchaseForm.locator('[name="purchaseDescription"]');
  await expect(firstPurchaseField).toBeFocused();
  for (let tab = 0; tab < 20; tab++) {
    if (await lastPurchaseField.evaluate((field) => field === document.activeElement)) break;
    await page.keyboard.press('Tab');
  }
  await expect(lastPurchaseField).toBeFocused();
  const modalScroll = await page.evaluate(() => {
    const content = document.querySelector('.finance-action-dialog .p-dialog-content');
    const field = content?.querySelector('[name="purchaseGroup"]');
    const contentRect = content?.getBoundingClientRect();
    const fieldRect = field?.getBoundingClientRect();
    return {
      scrollable: !!content && content.scrollHeight > content.clientHeight,
      scrollTop: content?.scrollTop ?? 0,
      fieldVisible:
        !!contentRect &&
        !!fieldRect &&
        fieldRect.top >= contentRect.top &&
        fieldRect.bottom <= contentRect.bottom,
    };
  });
  expect(modalScroll).toMatchObject({ scrollable: true, fieldVisible: true });
  expect(modalScroll.scrollTop).toBeGreaterThan(0);
  await page.setViewportSize({ width: 390, height: 844 });
  const purchase = waitForApi(page, 'POST', '/api/purchases');
  await purchaseForm.locator('[name="purchaseDescription"]').fill('Compra familiar');
  await purchaseForm.locator('[name="purchaseCategory"]').selectOption({ label: 'Sin clasificar' });
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

  await mobileNavigationButton(page, 'Compartidos').click();
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

  await mobileNavigationButton(page, 'Tarjetas').click();
  await page.locator('input[aria-label="Período a consultar"]').fill(month);
  await cardResource.getByRole('button', { name: 'Ver resumen y compras' }).click();
  let statement = await (
    await page.request.get(`/api/cards/${cardId}/statement?period=${month}&currency=ARS`)
  ).json();
  expect(statement.expectedAmount).toBe('40000.00');
  const statementForm = page.locator('form').filter({ has: page.locator('[name="actualTotal"]') });
  await page.getByRole('button', { name: 'Revisar resumen' }).click();
  await statementForm.locator('[name="actualTotal"]').fill('40000');
  const saveStatement = waitForApi(page, 'PUT', `/api/cards/${cardId}/statement`);
  await statementForm.getByRole('button', { name: 'Guardar revisión' }).click();
  expect((await saveStatement).status()).toBe(200);
  const confirmStatement = waitForApi(page, 'POST', `/api/cards/${cardId}/statement/confirm`);
  await statementForm.getByRole('button', { name: 'Confirmar resumen' }).click();
  expect((await confirmStatement).status()).toBe(200);
  const secondCardResource = page.locator('.resource-card').filter({ hasText: 'Visa secundaria' });
  const secondStatement = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return (
      response.request().method() === 'GET' &&
      /^\/api\/cards\/[^/]+\/statement$/.test(url.pathname)
    );
  });
  await secondCardResource.getByRole('button', { name: 'Ver resumen y compras' }).click();
  await secondStatement;
  await expect(statementForm).toBeHidden();
  await page.getByRole('button', { name: 'Revisar resumen' }).click();
  await expect(statementForm).toBeVisible();
  await page.keyboard.press('Escape');
  await cardResource.getByRole('button', { name: 'Ver resumen y compras' }).click();
  await page.getByRole('button', { name: 'Registrar pago' }).click();
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
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toBeHidden();
  statement = await (
    await page.request.get(`/api/cards/${cardId}/statement?period=${month}&currency=ARS`)
  ).json();
  expect(statement.pendingAmount).toBe('0.00');
  expect(statement.payments).toHaveLength(2);

  await mobileNavigationButton(page, 'Compartidos').click();
  const period = page.locator('input[aria-label="Período a consultar"]');
  const periodRequests: string[] = [];
  const trackPeriodRequests = (request: import('@playwright/test').Request) => {
    const url = new URL(request.url());
    if (
      request.method() === 'GET' &&
      (url.pathname === '/api/finance' ||
        /^\/api\/cards\/[^/]+\/statement$/.test(url.pathname) ||
        /^\/api\/groups\/[^/]+\/receivables$/.test(url.pathname))
    ) {
      periodRequests.push(`${url.pathname}?period=${url.searchParams.get('period') ?? ''}`);
    }
  };
  page.on('request', trackPeriodRequests);
  let releasePendingFinance!: () => void;
  let signalPendingFinance!: () => void;
  const pendingFinance = new Promise<void>((resolve) => { signalPendingFinance = resolve; });
  const continueFinance = new Promise<void>((resolve) => { releasePendingFinance = resolve; });
  await page.route('**/api/finance**', async (route) => {
    const url = new URL(route.request().url());
    if (url.searchParams.get('period') === nextMonth) {
      signalPendingFinance();
      await continueFinance;
    }
    await route.continue();
  });
  const nextOverview = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === '/api/finance' && url.searchParams.get('period') === nextMonth;
  });
  await period.fill(nextMonth);
  await pendingFinance;
  await expect(period).toBeDisabled();
  await expect(page.locator('app-cards h3').filter({ hasText: `Resumen ${month}` })).toHaveCount(1);
  await expect(page.locator('app-sharing .operation-row').first()).toContainText(month);
  expect(periodRequests.filter((request) => request.startsWith('/api/cards/') || request.startsWith('/api/groups/'))).toEqual([]);
  releasePendingFinance();
  expect((await nextOverview).status()).toBe(200);
  await expect.poll(() => periodRequests.filter((request) => request.startsWith('/api/cards/') || request.startsWith('/api/groups/')).length).toBe(2);
  await expect(page.locator('app-cards h3').filter({ hasText: `Resumen ${nextMonth}` })).toHaveCount(1);
  await page.unroute('**/api/finance**');
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

  await mobileNavigationButton(page, 'Tarjetas').click();
  await expect(page.getByRole('heading', { name: `Resumen ${month}` })).toBeVisible();
  await page.getByRole('button', { name: 'Cargar compra' }).click();
  const retainedPurchaseForm = page.locator('form').filter({ has: page.locator('[name="purchaseDescription"]') });
  await retainedPurchaseForm.locator('[name="purchaseDescription"]').fill('Borrador de período');
  await page.keyboard.press('Escape');
  await expect(retainedPurchaseForm).toBeHidden();

  await mobileNavigationButton(page, 'Compartidos').click();
  await page.getByRole('button', { name: 'Crear grupo' }).first().click();
  const retainedGroupForm = page.locator('form').filter({ has: page.locator('[name="groupName"]') });
  await retainedGroupForm.locator('[name="groupName"]').fill('Borrador de período');
  await page.keyboard.press('Escape');
  await expect(retainedGroupForm).toBeHidden();

  await mobileNavigationButton(page, 'Inicio').click();
  const periodForClear = page.getByLabel('Período a consultar');
  const periodErrorForClear = page.getByRole('alert').filter({ hasText: /período válido/i });
  periodRequests.length = 0;
  await periodForClear.fill('');
  await expect(periodErrorForClear).toBeVisible();
  await page.waitForTimeout(150);
  const cardError = page.locator('app-cards .feedback.error');
  const sharingError = page.locator('app-sharing .feedback.error');
  const invalidPeriodState = {
    cardError: await cardError.count() ? await cardError.innerText() : '',
    sharingError: await sharingError.count() ? await sharingError.innerText() : '',
    requests: [...periodRequests],
  };
  expect(invalidPeriodState).toEqual({ cardError: '', sharingError: '', requests: [] });

  const restoredOverview = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === '/api/finance' && url.searchParams.get('period') === month;
  });
  await periodForClear.fill(month);
  expect((await restoredOverview).status()).toBe(200);
  await expect(cardError).toHaveCount(0);
  await expect(sharingError).toHaveCount(0);
  expect(periodRequests).toContain(`/api/finance?period=${month}`);
  expect(periodRequests.some((request) => request.startsWith('/api/cards/') && request.endsWith(`?period=${month}`))).toBe(true);
  expect(periodRequests.some((request) => request.startsWith('/api/groups/') && request.endsWith(`?period=${month}`))).toBe(true);
  page.off('request', trackPeriodRequests);

  await mobileNavigationButton(page, 'Tarjetas').click();
  await page.getByRole('button', { name: 'Cargar compra' }).click();
  await expect(page.locator('form').filter({ has: page.locator('[name="purchaseDescription"]') }).locator('[name="purchaseDescription"]')).toHaveValue('Borrador de período');
  await page.keyboard.press('Escape');
  await mobileNavigationButton(page, 'Compartidos').click();
  await page.getByRole('button', { name: 'Crear grupo' }).first().click();
  await expect(page.locator('form').filter({ has: page.locator('[name="groupName"]') }).locator('[name="groupName"]')).toHaveValue('Borrador de período');
  await page.keyboard.press('Escape');
  await mobileNavigationButton(page, 'Inicio').click();

  await page.reload();
  await expect(page.locator('.balance-card.ars strong')).toContainText('76.900');
  const accountsPanel = page.locator('.accounts-panel');
  await expect(
    accountsPanel.locator('.account-row').filter({ hasText: 'Cuenta principal' }).locator('strong'),
  ).toHaveText(/\$\s*76\.800,00/);
  await expect(
    accountsPanel.locator('.account-row').filter({ hasText: 'Cuenta secundaria' }).locator('strong'),
  ).toHaveText(/\$\s*100,00/);
  await expect(page.locator('.debt-row')).toContainText('80.000');
  const homePhoneLayout = await page.evaluate(() => {
    const rect = (selector: string) =>
      document.querySelector(selector)?.getBoundingClientRect() ?? new DOMRect();
    const summary = rect('.summary-grid');
    const actions = rect('.quick-panel');
    const register = rect('.quick-panel .quick-actions button');
    const accounts = rect('.accounts-panel');
    const bottomNavigation = rect('nav.mobile-navigation');
    const period = rect('input[aria-label="Período a consultar"]');
    return {
      summaryTop: summary.top,
      summaryBottom: summary.bottom,
      actionsBottom: actions.bottom,
      registerBottom: register.bottom,
      accountsTop: accounts.top,
      navigationTop: bottomNavigation.top,
      periodWidth: period.width,
      periodValue: (document.querySelector('input[aria-label="Período a consultar"]') as HTMLInputElement)
        ?.value,
      viewportHeight: window.innerHeight,
    };
  });
  expect(homePhoneLayout.summaryTop).toBeGreaterThan(0);
  expect(homePhoneLayout.summaryBottom).toBeLessThanOrEqual(homePhoneLayout.viewportHeight);
  expect(homePhoneLayout.summaryBottom).toBeLessThan(homePhoneLayout.navigationTop);
  expect(homePhoneLayout.registerBottom).toBeLessThanOrEqual(homePhoneLayout.viewportHeight);
  expect(homePhoneLayout.registerBottom).toBeLessThan(homePhoneLayout.navigationTop);
  expect(homePhoneLayout.actionsBottom).toBeLessThan(homePhoneLayout.accountsTop);
  expect(homePhoneLayout.periodWidth).toBeGreaterThanOrEqual(170);
  expect(homePhoneLayout.periodValue).toMatch(/^\d{4}-\d{2}$/);
  await page.screenshot({ path: resolve(output, 'task2-finance-mobile-390.png'), fullPage: true });

  await page.setViewportSize({ width: 360, height: 800 });
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  await expect(page.getByText('Saldo en cuentas', { exact: true })).toBeVisible();
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth,
    navViewport: document.querySelector('nav.mobile-navigation')?.clientWidth ?? 0,
    navContent: document.querySelector('nav.mobile-navigation')?.scrollWidth ?? 0,
    mobileNavVisible: getComputedStyle(document.querySelector('nav.mobile-navigation')!).display !== 'none',
    navTargets: Array.from(document.querySelectorAll('nav.mobile-navigation button')).map((item) => {
      const rect = item.getBoundingClientRect();
      return { width: rect.width, height: rect.height };
    }),
    controls: Array.from(
      document.querySelectorAll(
        '.workspace button, .workspace input:not([type="checkbox"]), .workspace select, .workspace textarea',
      ),
    )
      .filter((item) => item.getBoundingClientRect().width > 0)
      .map((item) => ({
        label: item.getAttribute('aria-label') ?? item.textContent?.trim() ?? item.tagName,
        tagName: item.tagName,
        height: item.getBoundingClientRect().height,
        fontSize: Number.parseFloat(getComputedStyle(item).fontSize),
      })),
    desktopNavVisible: getComputedStyle(document.querySelector('nav.navigation')!).display !== 'none',
    bottomInset: getComputedStyle(document.querySelector('nav.mobile-navigation')!).paddingBottom,
  }));
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport);
  expect(dimensions.navContent).toBeLessThanOrEqual(dimensions.navViewport);
  expect(dimensions.desktopNavVisible).toBe(false);
  expect(dimensions.mobileNavVisible).toBe(true);
  expect(dimensions.navTargets.every((target) => target.width >= 44 && target.height >= 44)).toBe(true);
  expect(
    dimensions.controls.every((control) => control.height >= 44),
    JSON.stringify(dimensions.controls),
  ).toBe(true);
  expect(
    dimensions.controls
      .filter((control) => control.tagName !== 'BUTTON')
      .every((control) => control.fontSize >= 16),
  ).toBe(true);
  expect(dimensions.bottomInset).not.toBe('0px');
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  const clearContent = await page.evaluate(() => ({
    contentBottom: document.querySelector('.workspace > main')?.getBoundingClientRect().bottom ?? 0,
    navigationTop: document.querySelector('nav.mobile-navigation')?.getBoundingClientRect().top ?? 0,
  }));
  expect(clearContent.contentBottom).toBeLessThanOrEqual(clearContent.navigationTop);
  await page.screenshot({ path: resolve(output, 'task2-finance-mobile-360.png'), fullPage: true });
  const sharingNavigation = mobileNavigationButton(page, 'Compartidos');
  await sharingNavigation.scrollIntoViewIfNeeded();
  await sharingNavigation.click();
  await expect(page.getByRole('heading', { name: 'Repartos y cobros mensuales' })).toBeVisible();
  await expect(page.locator('.balance-card.ars')).toHaveCount(0);
  const sharingWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(sharingWidth).toBeLessThanOrEqual(360);
  await page.screenshot({ path: resolve(output, 'task2-sharing-mobile-360.png'), fullPage: true });

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.locator('nav.navigation').getByRole('button', { name: 'Inicio', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  await expect(page.getByText('Saldo en cuentas', { exact: true })).toBeVisible();
  const desktop = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth,
    navVisible: getComputedStyle(document.querySelector('nav.navigation')!).display !== 'none',
    mobileNavVisible:
      getComputedStyle(document.querySelector('nav.mobile-navigation')!).display !== 'none',
  }));
  expect(desktop.document).toBeLessThanOrEqual(desktop.viewport);
  expect(desktop.navVisible).toBe(true);
  expect(desktop.mobileNavVisible).toBe(false);
  await page.screenshot({ path: resolve(output, 'task2-finance-desktop-1280.png'), fullPage: true });
});

test('keeps populated home summary and primary action above the phone fold at both widths', async ({
  page,
}) => {
  await page.goto('/');
  const signIn = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
  expect((await signIn).status()).toBe(204);
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  await expect(page.locator('.summary-grid')).toBeVisible();

  for (const viewport of [
    { width: 390, height: 844 },
    { width: 360, height: 800 },
  ]) {
    await page.setViewportSize(viewport);
    const layout = await page.evaluate(() => {
      const rect = (selector: string) =>
        document.querySelector(selector)?.getBoundingClientRect() ?? new DOMRect();
      const period = document.querySelector(
        'input[aria-label="Período a consultar"]',
      ) as HTMLInputElement | null;
      return {
        summaryTop: rect('.summary-grid').top,
        summaryBottom: rect('.summary-grid').bottom,
        quickActionsBottom: rect('.quick-panel').bottom,
        registerBottom: rect('.quick-panel .quick-actions button').bottom,
        accountListTop: rect('.accounts-panel').top,
        navigationTop: rect('nav.mobile-navigation').top,
        periodWidth: period?.getBoundingClientRect().width ?? 0,
        periodValue: period?.value,
        viewportHeight: window.innerHeight,
        phoneNavigationVisible:
          getComputedStyle(document.querySelector('nav.mobile-navigation')!).display !== 'none',
      };
    });
    expect.soft(layout.summaryTop).toBeGreaterThan(0);
    expect.soft(layout.summaryBottom).toBeLessThanOrEqual(layout.viewportHeight);
    expect.soft(layout.summaryBottom).toBeLessThan(layout.navigationTop);
    expect.soft(layout.registerBottom).toBeLessThanOrEqual(layout.viewportHeight);
    expect.soft(layout.registerBottom).toBeLessThan(layout.navigationTop);
    expect.soft(layout.quickActionsBottom).toBeLessThan(layout.accountListTop);
    expect.soft(layout.periodWidth).toBeGreaterThanOrEqual(170);
    expect.soft(layout.periodValue).toMatch(/^\d{4}-\d{2}$/);
    expect.soft(layout.phoneNavigationVisible).toBe(true);
  }
});

test('keeps live ARS and USD balances readable with a long workspace name on phones', async ({ page }) => {
  await page.goto('/');
  const signIn = waitForApi(page, 'POST', '/api/auth/development');
  await page.getByRole('button', { name: 'Ingresar como desarrollador' }).click();
  expect((await signIn).status()).toBe(204);
  await expect(page.getByRole('heading', { name: 'Inicio', exact: true })).toBeVisible();
  await expect(page.locator('.summary-grid')).toBeVisible();

  const workspaceIdInput = randomUUID();
  const name = `Hogar de prueba con un nombre deliberadamente extenso para revisar el selector ${workspaceIdInput.slice(0, 6)}`;
  const created = await page.evaluate(async ({ id, name }) => {
    const session = await (await fetch('/api/auth/session')).json();
    const response = await fetch('/api/workspaces', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken },
      body: JSON.stringify({ id, kind: 'Household', name }),
    });
    return { status: response.status, workspace: await response.json() };
  }, { id: workspaceIdInput, name });
  expect(created.status).toBe(201);
  const workspaceId = created.workspace.id as string;

  await page.reload();
  await expect(page.locator('.workspace-picker select')).toBeVisible();
  await page.locator('.workspace-picker select').selectOption(workspaceId);
  await expect(page.locator('.workspace-picker select')).toHaveValue(workspaceId);
  const date = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Argentina/Buenos_Aires',
  }).format(new Date());
  const accounts = await page.evaluate(async ({ workspaceId, date }) => {
    const session = await (await fetch('/api/auth/session')).json();
    const baseHeaders = {
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': session.csrfToken,
      'X-Workspace-Id': workspaceId,
    };
    const resources = [
      { name: 'Reserva en pesos', currency: 'ARS', openingBalance: '9999999999.99' },
      { name: 'Fondo en dólares', currency: 'USD', openingBalance: '123456789.12' },
    ];
    const responses = [];
    for (const resource of resources) {
      const response = await fetch('/api/accounts', {
        method: 'POST',
        headers: baseHeaders,
        body: JSON.stringify({
          id: crypto.randomUUID(),
          bankName: 'Banco de prueba',
          openingDate: date,
          ...resource,
        }),
      });
      responses.push(response.status);
    }
    return responses;
  }, { workspaceId, date });
  expect(accounts).toEqual([200, 200]);

  await page.reload();
  await expect(page.locator('.summary-grid')).toBeVisible();
  await expect(page.getByText('Saldo en cuentas', { exact: true })).toBeVisible();
  await expect(page.locator('.workspace-picker select')).toHaveValue(workspaceId);
  const pesos = page.getByRole('article', { name: 'Saldo en pesos argentinos (ARS)' });
  const dollars = page.getByRole('article', { name: 'Saldo en dólares estadounidenses (USD)' });
  await expect(pesos).toBeVisible();
  await expect(pesos.getByText('ARS', { exact: true })).toBeVisible();
  await expect(dollars).toBeVisible();
  await expect(dollars.getByText('USD', { exact: true })).toBeVisible();

  for (const viewport of [
    { width: 360, height: 844 },
    { width: 390, height: 844 },
  ]) {
    await page.setViewportSize(viewport);
    const layout = await page.evaluate(() => {
      const bounds = (element: Element | null) => {
        const rect = element?.getBoundingClientRect() ?? new DOMRect();
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
      };
      const amounts = [...document.querySelectorAll<HTMLElement>('.balance-summary .summary-card strong')];
      const nav = document.querySelector('nav.mobile-navigation');
      const accountHeading = [...document.querySelectorAll('.accounts-panel h3')].find(item => item.textContent?.includes('Tus cuentas')) ?? null;
      const register = [...document.querySelectorAll<HTMLButtonElement>('.quick-panel button')].find(button => button.textContent?.includes('Registrar movimiento')) ?? null;
      return {
        viewport: window.innerWidth,
        documentWidth: document.documentElement.scrollWidth,
        balances: amounts.map(amount => ({ ...bounds(amount), contentWidth: amount.scrollWidth, innerWidth: amount.clientWidth, text: amount.textContent })),
        register: bounds(register),
        monthlyQuota: bounds([...document.querySelectorAll('.finance-row')].find(row => row.textContent?.includes('Cuotas de tarjeta del mes') ?? false) ?? null),
        cardDebt: bounds(document.querySelector('.debt-row')),
        accounts: bounds(accountHeading),
        navigation: bounds(nav),
      };
    });
    expect(layout.documentWidth, JSON.stringify(layout)).toBeLessThanOrEqual(viewport.width);
    expect(layout.balances).toHaveLength(2);
    for (const amount of layout.balances) {
      expect(amount.left, JSON.stringify(layout)).toBeGreaterThanOrEqual(0);
      expect(amount.right, JSON.stringify(layout)).toBeLessThanOrEqual(viewport.width);
      expect(amount.contentWidth, JSON.stringify(layout)).toBeLessThanOrEqual(amount.innerWidth);
    }
    expect(layout.register.bottom, JSON.stringify(layout)).toBeLessThan(layout.navigation.top);
    expect(layout.monthlyQuota.top, JSON.stringify(layout)).toBeLessThan(layout.accounts.top);
    expect(layout.monthlyQuota.bottom, JSON.stringify(layout)).toBeLessThanOrEqual(layout.navigation.top);
    expect(layout.cardDebt.bottom, JSON.stringify(layout)).toBeLessThanOrEqual(layout.navigation.top);
    await page.screenshot({
      path: resolve(process.cwd(), `../work/task1-panorama-live-${viewport.width}.png`),
      fullPage: true,
    });
  }

  await page.setViewportSize({ width: 1280, height: 900 });
  const desktop = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    page: document.documentElement.scrollWidth,
    homeColumns: getComputedStyle(document.querySelector('.dashboard-columns')!).gridTemplateColumns,
    mobileNavigation: getComputedStyle(document.querySelector('nav.mobile-navigation')!).display,
  }));
  expect(desktop.page).toBeLessThanOrEqual(desktop.viewport);
  expect(desktop.homeColumns.split(' ').length).toBe(2);
  expect(desktop.mobileNavigation).toBe('none');
  await page.screenshot({ path: resolve(process.cwd(), '../work/task1-panorama-live-desktop-1280.png'), fullPage: true });
});
