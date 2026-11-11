import { Buffer } from 'node:buffer';
import { expect, test, type Page } from '@playwright/test';

const session = {
  id: 'desktop-test', userName: 'secretariat', displayName: 'Administrateur du secrétariat',
  role: 'Admin', roleLabel: 'Administrateur', permissions: [], mustChangePassword: false,
  schoolName: 'Institut Technique Médical', currentYear: '2026-2027', currencySymbol: 'FC', phoneCountryCode: '243',
};

async function connect(page: Page, overrides: Partial<typeof session> = {}) {
  await page.route('**/api/installation', (route) => route.fulfill({ json: { isConfigured: true } }));
  await page.route('**/api/session', (route) => route.fulfill({ json: { ...session, ...overrides } }));
}

test('préférences : garder le focus, contenir le clavier et retrouver le bouton d’ouverture', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  await connect(page);
  await page.goto('/profil');
  await expect(page.getByRole('heading', { name: 'Mon mot de passe' })).toBeVisible();
  await page.getByRole('tab', { name: 'Affichage', exact: true }).click();
  const opener = page.getByRole('button', { name: 'Apparence : Ouvre toutes les préférences d’affichage.', exact: true });
  await opener.click();
  const dialog = page.getByRole('dialog', { name: 'Préférences d’affichage' });
  const large = dialog.getByRole('button', { name: 'Très grand', exact: true });
  await large.click();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--fs').trim())).toBe('1.3');
  await expect(large).toBeFocused();

  const close = dialog.getByRole('button', { name: 'Fermer', exact: true });
  const done = dialog.getByRole('button', { name: 'Terminé', exact: true });
  await done.focus();
  await page.keyboard.press('Tab');
  await expect(close).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(done).toBeFocused();
  await page.keyboard.press('Control+k');
  await expect(page.getByRole('dialog')).toHaveCount(1);
  await page.keyboard.press('Alt+2');
  await expect(page).toHaveURL(/\/profil$/);
  const box = await dialog.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.y).toBeGreaterThanOrEqual(0);
  expect(box!.y + box!.height).toBeLessThanOrEqual(721);
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(opener).toBeFocused();
});

test('palette au clavier : le résultat actif reste visible et le focus revient à l’ouverture', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 600 });
  await connect(page);
  await page.route('**/api/recherche?**', (route) => route.fulfill({ json: Array.from({ length: 14 }, (_, i) => ({
    type: 'Apprenant', title: `ZZDESKTOP ${i + 1} · ${'Dossier complet '.repeat(8)}`,
    subtitle: 'Informations complémentaires pour distinguer les résultats de la recherche.', url: `/profil?dossier=${i + 1}`,
  })) }));
  await page.goto('/profil');
  await expect(page.getByRole('heading', { name: 'Mon mot de passe' })).toBeVisible();
  const opener = page.getByRole('button', { name: 'Ouvrir les commandes (Ctrl+K)', exact: true });
  await opener.click();
  const dialog = page.getByRole('dialog', { name: 'Recherche globale' });
  const search = dialog.getByPlaceholder('Nom, matricule, n° de reçu ou page…');
  await expect(search).toBeFocused();
  await search.fill('zzdesktop');
  await expect(dialog.locator('li')).toHaveCount(14);
  for (let i = 0; i < 13; i++) await search.press('ArrowDown');
  const active = dialog.locator('li.on');
  await expect(active).toContainText('ZZDESKTOP 14');
  const bounds = await dialog.locator('ul').boundingBox();
  const selected = await active.boundingBox();
  expect(bounds).not.toBeNull();
  expect(selected).not.toBeNull();
  expect(selected!.y).toBeGreaterThanOrEqual(bounds!.y - 1);
  expect(selected!.y + selected!.height).toBeLessThanOrEqual(bounds!.y + bounds!.height + 1);
  await search.press('Tab');
  await expect(search).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(opener).toBeFocused();
});

test('bureau : texte agrandi, zoom à 140 % et notification longue sans débordement', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('scolia.zoom', '140');
    localStorage.setItem('scolia.taille', 'tres-grand');
  });
  await connect(page, { displayName: 'Administrateur'.repeat(30) });
  const school = {
    name: session.schoolName, shortName: 'ITM', currencyCode: 'CDF', currencySymbol: 'FC',
    phoneCountryCode: '243', wholeAmountsOnly: true, hasLogo: false,
  };
  const longError = `Le_document_${'transmis_ne_peut_pas_être_traité_'.repeat(18)}`;
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6ZF8AAAAASUVORK5CYII=', 'base64');
  await page.route('**/api/etablissement', (route) => route.fulfill({ json: school }));
  await page.route('**/api/etablissement/logo*', (route) => route.request().method() === 'PUT'
    ? route.fulfill({ status: 400, json: { title: longError } })
    : route.fulfill({ contentType: 'image/png', body: png }));

  for (const width of [1440, 1024]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/parametres');
    await expect(page.getByLabel('Nom', { exact: true })).toHaveValue(school.name);
    await expect.poll(() => page.locator('.content').evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  }

  await page.locator('input[type="file"]').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: png });
  const toast = page.locator('.toast.error');
  await expect(toast).toContainText(longError);
  await expect.poll(() => toast.evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
  const box = await toast.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(1025);
});

test('fenêtre métier : couvrir la fenêtre même avec le zoom du contenu et garder les actions visibles', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('scolia.zoom', '140');
    localStorage.setItem('scolia.taille', 'tres-grand');
  });
  await connect(page);
  await page.route('**/api/utilisateurs', (route) => route.fulfill({ json: [] }));
  for (const viewport of [{ width: 1280, height: 720 }, { width: 1024, height: 400 }]) {
    await page.setViewportSize(viewport);
    await page.goto('/utilisateurs');
    const opener = page.getByRole('button', { name: 'Nouveau compte', exact: true });
    await opener.click();
    const dialog = page.getByRole('dialog', { name: 'Nouveau compte', exact: true });
    await expect(dialog.getByLabel('Nom complet')).toBeFocused();
    await dialog.getByLabel('Nom complet').fill('Nouveau collègue');
    await expect(dialog.getByLabel('Nom complet')).toBeFocused();
    await expect(dialog.getByRole('button', { name: 'Annuler', exact: true })).toBeInViewport();
    await expect(dialog.getByRole('button', { name: 'Enregistrer', exact: true })).toBeInViewport();
    const overlay = await page.locator('.overlay').boundingBox();
    expect(overlay).not.toBeNull();
    expect(overlay!.x).toBeCloseTo(0, 0);
    expect(overlay!.y).toBeCloseTo(0, 0);
    expect(overlay!.width).toBeCloseTo(viewport.width, 0);
    expect(overlay!.height).toBeCloseTo(viewport.height, 0);
    const bounds = await dialog.boundingBox();
    expect(bounds!.y).toBeGreaterThanOrEqual(0);
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height + 1);
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
  }
});
