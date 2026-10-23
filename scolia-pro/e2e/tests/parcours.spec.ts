import { expect, test, type Page } from '@playwright/test';

test.describe.configure({ mode: 'serial' });

const admin = { user: 'admin', password: 'Scolia2026!' };
let page: Page;

const baseURL = `http://localhost:${process.env.SCOLIA_E2E_PORT ?? 5199}`;

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage({ baseURL, locale: 'fr-FR', viewport: { width: 1440, height: 900 } });
  page.on('pageerror', (error) => { throw error; });
});

test('installation guidée avec données de démonstration', async () => {
  await page.goto('/');
  await expect(page).toHaveURL(/installation/);
  await page.getByLabel("Nom de l'établissement").fill('Institut Technique Médical Démo');
  await page.getByLabel('Sigle').fill('ITM Démo');
  await page.getByLabel('Nom complet').fill('Administrateur Démo');
  await page.getByLabel('Mot de passe', { exact: true }).fill(admin.password);
  await page.getByLabel('Confirmer le mot de passe').fill(admin.password);
  await page.getByText('Ajouter des données de démonstration').click();
  await page.getByRole('button', { name: 'Installer SCOLIA' }).click();
  await expect(page.getByRole('heading', { name: 'Tableau de bord' })).toBeVisible();
  await expect(page.getByText('Apprenants actifs')).toBeVisible();
  await expect(page.locator('.kpi').first()).toContainText('128');
});

test('recherche et fiche apprenant', async () => {
  await page.getByRole('link', { name: 'Apprenants' }).click();
  await page.getByLabel('Recherche').fill('INF101');
  await expect(page.locator('table.data tbody tr')).toHaveCount(1);
  await page.locator('table.data tbody tr').first().click();
  await expect(page.getByText('Situation financière')).toBeVisible();
  await expect(page.locator('.tag-code').first()).toHaveText('INF101');
});

test('inscription d’un nouvel apprenant avec matricule automatique', async () => {
  await page.goto('/apprenants');
  await page.getByRole('button', { name: 'Nouvelle inscription' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Nom', { exact: true }).fill('Lukusa');
  await dialog.getByLabel('Post-nom').fill('Tshimanga');
  await dialog.getByLabel('Prénom').fill('esther');
  await dialog.getByLabel('Sexe').selectOption('F');
  await dialog.getByLabel('Contact parent 1').fill('0812345678');
  await dialog.getByLabel('Promotion').selectOption({ label: 'TLM1 — Première année' });
  await dialog.getByRole('button', { name: 'Enregistrer l’inscription' }).click();
  await expect(page.getByRole('heading', { name: 'LUKUSA TSHIMANGA Esther' })).toBeVisible();
  await expect(page.locator('.tag-code').first()).toHaveText(/^TLM1\d{2}$/);
});

test('encaissement réparti et reçu numéroté', async () => {
  await page.getByRole('button', { name: 'Encaisser' }).first().click();
  await expect(page).toHaveURL(/encaisser\?inscription=/);
  await page.getByLabel(/Montant versé/).fill('75000');
  await expect(page.getByText('Frais d\'inscription').last()).toBeVisible();
  await expect(page.locator('table.data tfoot')).toContainText('75 000');
  await page.getByRole('button', { name: /Valider le paiement/ }).click();
  await expect(page.getByText('Paiement enregistré')).toBeVisible();
  await expect(page.locator('.tag-code', { hasText: /RC-\d{4}-\d{6}/ })).toBeVisible();
});

test('trop-perçu refusé avec message clair', async () => {
  await page.getByRole('button', { name: 'Autre paiement pour cet apprenant' }).click();
  await page.getByLabel(/Montant versé/).fill('99999999');
  await expect(page.getByText(/dépasse le reste à payer/)).toBeVisible();
  await expect(page.getByRole('button', { name: /Valider le paiement/ })).toBeDisabled();
});

test('journal des reçus, annulation motivée et vérification publique', async () => {
  await page.goto('/paiements');
  await page.locator('table.data tbody tr').first().click();
  await expect(page.getByRole('heading', { name: /Reçu RC-/ })).toBeVisible();
  const code = await page.locator('dt:has-text("Code de vérification") + dd').innerText();
  await page.getByRole('button', { name: 'Annuler ce reçu' }).click();
  await page.getByLabel('Motif (obligatoire)').fill('Erreur de saisie du montant');
  await page.getByRole('dialog').getByRole('button', { name: 'Annuler le reçu' }).click();
  await expect(page.getByText('Reçu annulé', { exact: false }).first()).toBeVisible();
  await page.goto(`/verifier/${code}`);
  await expect(page.getByText('Reçu annulé')).toBeVisible();
  await page.goto('/verifier/FAUXCODE00');
  await expect(page.getByText('Reçu introuvable')).toBeVisible();
});

test('recouvrement avec rappels WhatsApp', async () => {
  await page.goto('/recouvrement');
  await expect(page.locator('table.data tbody tr').first()).toBeVisible();
  const link = page.locator('a[href^="https://wa.me/243"]').first();
  await expect(link).toBeVisible();
});

test('palette de commandes Ctrl+K', async () => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Tableau de bord' })).toBeVisible();
  await page.locator('body').click({ position: { x: 900, y: 120 } });
  await page.keyboard.press('Control+k');
  await page.getByPlaceholder(/Nom, matricule/).fill('caisse');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Caisse' })).toBeVisible();
});

test('notes : saisie et résultats', async () => {
  await page.goto('/notes');
  await expect(page.getByText('Notes ·')).toBeVisible();
  const first = page.locator('.grade-input').first();
  await first.fill('16');
  await page.getByRole('button', { name: 'Enregistrer' }).click();
  await expect(page.getByText(/note\(s\) enregistrée\(s\)/)).toBeVisible();
  await page.getByText('Résultats et bulletins').click();
  await expect(page.locator('table.data tbody tr').first()).toBeVisible();
});

test('déconnexion puis verrouillage après cinq erreurs', async () => {
  await page.getByRole('button', { name: /Administrateur Démo/ }).click();
  await page.getByRole('button', { name: 'Se déconnecter' }).click();
  await expect(page).toHaveURL(/connexion/);
  for (let i = 0; i < 5; i++) {
    await page.getByLabel('Identifiant').fill(admin.user);
    await page.getByLabel('Mot de passe').fill('mauvais-mot');
    await page.getByRole('button', { name: 'Se connecter' }).click();
    await expect(page.locator('.alert.error')).toBeVisible();
  }
  await page.getByLabel('Mot de passe').fill(admin.password);
  await page.getByRole('button', { name: 'Se connecter' }).click();
  await expect(page.locator('.alert.error')).toContainText('verrouillé');
});
