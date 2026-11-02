import { expect, test, type Locator } from '@playwright/test';

async function contrast(locator: Locator, placeholder = false) {
  return locator.evaluate((element, usePlaceholder) => {
    type Color = [number, number, number, number];
    const parse = (value: string): Color => {
      const parts = value.match(/[\d.]+/g)?.map(Number) ?? [];
      return [parts[0] ?? 0, parts[1] ?? 0, parts[2] ?? 0, parts[3] ?? 1];
    };
    const over = (fg: Color, bg: Color): Color => [
      fg[0] * fg[3] + bg[0] * (1 - fg[3]), fg[1] * fg[3] + bg[1] * (1 - fg[3]), fg[2] * fg[3] + bg[2] * (1 - fg[3]), 1,
    ];
    const ancestors: Element[] = [];
    for (let parent: Element | null = element; parent; parent = parent.parentElement) ancestors.unshift(parent);
    let bg: Color = [255, 255, 255, 1];
    for (const parent of ancestors) bg = over(parse(getComputedStyle(parent).backgroundColor), bg);
    const style = getComputedStyle(element, usePlaceholder ? '::placeholder' : undefined);
    const foreground = parse(style.color);
    foreground[3] *= Number(style.opacity);
    const fg = over(foreground, bg);
    const luminance = (c: Color) => c.slice(0, 3).map((channel) => {
      const value = channel / 255;
      return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
    }).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
    const a = luminance(fg), b = luminance(bg);
    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
  }, placeholder);
}

for (const width of [360, 1280]) {
  test(`thème sombre : connexion et icône de validation lisibles à ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await page.addInitScript(() => localStorage.setItem('scolia.theme', 'sombre'));
    await page.route('**/api/installation', (route) => route.fulfill({ json: { isConfigured: true } }));
    await page.route('**/api/session', (route) => route.fulfill({ status: 401, json: { message: 'Connexion requise' } }));
    await page.goto('/connexion');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await page.getByLabel('Identifiant').fill('caisse');
    await page.getByLabel('Mot de passe').fill('Scolia2026!');
    for (const locator of [page.locator('.signin-title'), page.locator('.signin-sub'), page.getByLabel('Identifiant'), page.getByRole('button', { name: 'Se connecter', exact: true })])
      expect(await contrast(locator)).toBeGreaterThanOrEqual(4.5);
    expect(await contrast(page.getByLabel('Identifiant'), true)).toBeGreaterThanOrEqual(4.5);
    expect(await page.locator('html').evaluate((el) => getComputedStyle(el).colorScheme)).toBe('dark');
  });

  test(`thème sombre : formulaire, menu et préférences après basculement à ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await page.addInitScript(() => localStorage.setItem('scolia.theme', 'sombre'));
    await page.route('**/api/installation', (route) => route.fulfill({ json: { isConfigured: true } }));
    await page.route('**/api/session', (route) => route.fulfill({ json: {
      id: 'contrast-test', userName: 'caisse', displayName: 'Amina Kabeya', role: 'Admin', roleLabel: 'Administration', permissions: [],
      mustChangePassword: false, schoolName: 'Institut de test', currencySymbol: 'FC', phoneCountryCode: '243',
    } }));
    await page.goto('/profil');
    await expect(page.getByRole('heading', { name: 'Mon mot de passe' })).toBeVisible();
    for (const selector of ['h1', '.field label', '.field .help', '.btn.primary', '.rb-file', '.user-menu .avatar']) {
      const locator = page.locator(selector).first();
      await expect(locator).toBeVisible();
      expect(await contrast(locator), selector).toBeGreaterThanOrEqual(4.5);
    }
    await page.getByRole('tab', { name: 'Affichage', exact: true }).click();
    await page.getByRole('button', { name: 'Apparence : Ouvre toutes les préférences d’affichage.', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Préférences d’affichage' });
    for (const theme of ['Clair', 'Sombre']) {
      await dialog.getByRole('button', { name: theme, exact: true }).click();
      await expect(page.locator('html')).toHaveAttribute('data-theme', theme === 'Sombre' ? 'dark' : 'light');
      for (const selector of ['h2', '.label', '.segmented button.on', '.btn.primary'])
        await expect.poll(() => contrast(dialog.locator(selector).first()), { message: `${theme} ${selector}` }).toBeGreaterThanOrEqual(4.5);
    }
  });
}
