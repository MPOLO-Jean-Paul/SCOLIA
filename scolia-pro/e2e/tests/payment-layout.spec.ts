import { expect, test } from '@playwright/test';

for (const viewport of [{ width: 320, height: 360 }, { width: 390, height: 640 }, { width: 1024, height: 520 }]) {
  test(`paiement en retard : actions accessibles à ${viewport.width}×${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const enrollmentId = '11111111-1111-1111-1111-111111111111';
    const account = {
      studentId: '22222222-2222-2222-2222-222222222222', enrollmentId, matricule: 'SC-2026-00001',
      fullName: 'Kabeya Mbuyi Amina du secrétariat des sciences de la santé', promotionCode: 'INF1', academicYear: '2026-2027', enrollmentStatus: 'Actif',
      totalFees: 1500000, totalCovered: 0, totalNet: 1500000, totalPaid: 0, balance: 1500000, overdue: 1500000, overdueCount: 12,
      agingBucket: '90+', receipts: [], guardianPhone: '0812345678',
      lines: Array.from({ length: 12 }, (_, i) => ({ feeItemId: `mois-${i}`, label: `Mensualité de septembre — période de rattrapage ${i + 1}`,
        dueDate: '2026-01-01', amount: 125000, covered: 0, net: 125000, paid: 0, remaining: 125000, status: 'En retard', isOverdue: true, kind: 'Mensualite' })),
    };
    await page.route('**/api/installation', (route) => route.fulfill({ json: { isConfigured: true } }));
    await page.route('**/api/session', (route) => route.fulfill({ json: {
      id: 'caisse-test', userName: 'caisse', displayName: 'Caisse de test', role: 'Admin', roleLabel: 'Administration', permissions: [],
      mustChangePassword: false, schoolName: 'Institut de test', currencySymbol: 'FC', phoneCountryCode: '243',
    } }));
    await page.route('**/api/apprenants?**', (route) => route.fulfill({ json: { items: [{ id: account.studentId, enrollmentId,
      matricule: account.matricule, lastName: 'Kabeya', middleName: 'Mbuyi', firstName: 'Amina', promotionCode: 'INF1',
      status: 'Actif', balance: account.balance, overdue: account.overdue }], total: 1, page: 1, pageSize: 12 } }));
    await page.route(`**/api/inscriptions/${enrollmentId}/compte`, (route) => route.fulfill({ json: account }));
    await page.route('**/api/paiements/apercu', (route) => {
      const body = route.request().postDataJSON();
      return route.fulfill({ json: { allocations: account.lines.filter((line) => body.feeItemIds.includes(line.feeItemId))
        .map((line) => ({ feeItemId: line.feeItemId, label: line.label, amount: line.remaining, remainingAfter: 0 })), balanceAfter: account.balance - body.amount } });
    });
    let posts = 0;
    let release!: () => void;
    const ready = new Promise<void>((resolve) => { release = resolve; });
    await page.route('**/api/paiements', async (route) => {
      posts++;
      const body = route.request().postDataJSON();
      await ready;
      return route.fulfill({ json: { id: 'recu-test', number: 'TEST-001', verificationCode: 'test', date: '2026-10-29',
        studentId: account.studentId, matricule: account.matricule, studentName: account.fullName, promotionCode: 'INF1',
        academicYear: '2026-2027', amount: body.amount, method: body.method, status: 'Valide', allocations: [], createdAtUtc: '2026-10-29T12:00:00Z' } });
    });
    await page.goto('/encaisser');
    const search = page.getByPlaceholder('Matricule ou nom (2 caractères minimum)');
    await expect(search).not.toBeFocused();
    await search.fill('Kabeya');
    await page.getByRole('button', { name: /SC-2026-00001/ }).click();
    await expect(page.locator('.payment-month')).toHaveCount(12);
    await page.locator('.payment-month input').nth(0).check();
    await page.locator('.payment-month input').nth(1).check();
    await page.locator('.payment-month input').nth(2).check();
    const pay = page.getByRole('button', { name: /Valider le paiement/ });
    await expect(pay).toBeEnabled();
    await pay.scrollIntoViewIfNeeded();
    const rect = await pay.boundingBox();
    expect(rect!.x).toBeGreaterThanOrEqual(0);
    expect(rect!.x + rect!.width).toBeLessThanOrEqual(viewport.width + 1);
    expect(rect!.height).toBeGreaterThanOrEqual(44);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await pay.click();
    try {
      await expect.poll(() => posts).toBe(1);
      await expect(pay).toBeDisabled();
      await pay.evaluate((button: HTMLButtonElement) => button.click());
      expect(posts).toBe(1);
    } finally { release(); }
    await expect(page.getByRole('heading', { name: 'Paiement enregistré' })).toBeVisible();
    for (const name of ['Imprimer le reçu', 'Autre paiement pour cet apprenant', 'Autre apprenant', 'Détail du reçu']) {
      const button = page.getByRole('button', { name, exact: true });
      await button.scrollIntoViewIfNeeded();
      const bounds = await button.boundingBox();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width + 1);
      await expect(button).toBeInViewport();
    }
    expect(posts).toBe(1);
  });
}
