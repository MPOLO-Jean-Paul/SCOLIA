import { expect, test } from '@playwright/test';

for (const width of [320, 360, 390]) {
  test(`connexion compacte à ${width}px, sans clavier imposé ni débordement`, async ({ page }) => {
    await page.setViewportSize({ width, height: 640 });
    await page.route('**/api/installation', (route) => route.fulfill({ json: { isConfigured: true } }));
    await page.route('**/api/session', (route) => route.fulfill({ status: 401, json: { message: 'Connexion requise' } }));
    await page.goto('/connexion');
    await expect(page.getByLabel('Identifiant')).toBeVisible();
    await expect(page.getByLabel('Identifiant')).not.toBeFocused();
    expect(await page.getByLabel('Mot de passe').evaluate((el) => parseFloat(getComputedStyle(el).fontSize))).toBeGreaterThanOrEqual(16);
    for (const name of ['Se connecter', 'Afficher la saisie']) {
      const box = await page.getByRole('button', { name, exact: true }).boundingBox();
      expect(box?.width).toBeGreaterThanOrEqual(44);
      expect(box?.height).toBeGreaterThanOrEqual(44);
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    // Clavier / paysage : le formulaire défile et le pied de page reste après son contenu.
    await page.setViewportSize({ width, height: 360 });
    const help = await page.locator('.signin-help').boundingBox();
    const footer = await page.locator('.signin-foot').boundingBox();
    expect(footer!.y).toBeGreaterThanOrEqual(help!.y + help!.height);
  });
}

test('logo visible pendant la vérification de la première installation', async ({ page }) => {
  let release!: () => void;
  const ready = new Promise<void>((resolve) => { release = resolve; });
  await page.route('**/api/installation', async (route) => {
    await ready;
    await route.fulfill({ json: { isConfigured: false } });
  });
  await page.goto('/installation');
  try {
    await expect(page.locator('.startup-screen')).toBeVisible();
    await expect(page.locator('.startup-screen').getByRole('img', { name: 'SCOLIA' })).toBeVisible();
    await expect(page.locator('.loading-dots span')).toHaveCount(3);
  } finally { release(); }
  await expect(page.getByRole('heading', { name: 'Configurer SCOLIA' })).toBeVisible();
});

test('bienvenue après authentification, une seule requête malgré deux validations', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 640 });
  await page.clock.install();
  await page.clock.pauseAt(new Date(Date.now() + 1000));
  let posts = 0;
  let release!: () => void;
  const ready = new Promise<void>((resolve) => { release = resolve; });
  const session = { id: 'mobile-test', userName: 'secretariat', displayName: 'Secrétariat', role: 'Admin', roleLabel: 'Administrateur', permissions: [], mustChangePassword: true, schoolName: 'SCOLIA', currencySymbol: 'FC', phoneCountryCode: '243' };
  await page.route('**/api/installation', (route) => route.fulfill({ json: { isConfigured: true } }));
  await page.route('**/api/session', async (route) => {
    if (route.request().method() !== 'POST') return route.fulfill({ status: 401, json: { message: 'Connexion requise' } });
    posts++;
    await ready;
    return route.fulfill({ json: session });
  });
  await page.goto('/connexion');
  await page.getByLabel('Identifiant').fill('secretariat');
  await page.getByLabel('Mot de passe').fill('Scolia2026!');
  await page.getByRole('button', { name: 'Se connecter' }).click();
  try {
    await expect(page.getByRole('button', { name: 'Se connecter' })).toBeDisabled();
    await expect.poll(() => posts).toBe(1);
    await page.locator('.signin-form').evaluate((form) => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    expect(posts).toBe(1);
  } finally { release(); }
  await expect(page.getByRole('heading', { name: 'Bienvenue' })).toBeVisible();
  await page.clock.runFor(500);
  await expect(page).toHaveURL(/\/profil$/);
  expect(posts).toBe(1);
});

test('les points de chargement respectent la réduction des animations', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  let release!: () => void;
  const ready = new Promise<void>((resolve) => { release = resolve; });
  await page.route('**/api/installation', async (route) => {
    await ready;
    await route.fulfill({ json: { isConfigured: false } });
  });
  await page.goto('/installation');
  try {
    await expect(page.locator('.loading-dots span').first()).toBeVisible();
    expect(await page.locator('.loading-dots span').first().evaluate((el) => getComputedStyle(el).animationName)).toBe('none');
  } finally { release(); }
});
