import { expect, test, type Page } from '@playwright/test';

test.describe.configure({ mode: 'serial' });

const admin = { user: 'secretariat', password: 'Scolia2026!' };
// Les champs obligatoires portent un astérisque : « Nom * ». Les recherches exactes l'acceptent.
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
  await page.getByLabel(/^Mot de passe( \*)?$/).fill(admin.password);
  await page.getByLabel('Confirmer le mot de passe').fill(admin.password);
  await page.getByText('Ajouter des données de démonstration').click();
  await page.getByRole('button', { name: 'Installer SCOLIA' }).click();
  await expect(page.getByRole('heading', { name: 'Tableau de bord' })).toBeVisible();
  await expect(page.getByText('Apprenants actifs')).toBeVisible();
  await expect(page.locator('.kpi').first()).toContainText('128');
});

test('recherche et fiche apprenant', async () => {
  await page.getByRole('tab', { name: 'Secrétariat' }).click();
  await page.getByRole('button', { name: 'Apprenants', exact: true }).click();
  await page.getByLabel('Recherche').fill('INF101');
  await expect(page.locator('table.data tbody tr')).toHaveCount(1);
  await page.locator('table.data tbody tr').first().click();
  await expect(page.getByText('Situation financière')).toBeVisible();
  await expect(page.locator('.tag-code').first()).toHaveText('INF101');
});

// Nom absent des données de démonstration, tirées au hasard : pas d'homonyme possible.
test('inscription d’un nouvel apprenant avec matricule automatique', async () => {
  await page.goto('/apprenants');
  await page.getByRole('button', { name: 'Nouvelle inscription' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Nom( \*)?$/).fill('Tshiamala');
  await dialog.getByLabel('Post-nom').fill('Kanyinda');
  await dialog.getByLabel('Prénom').fill('esther');
  await dialog.getByLabel('Sexe').selectOption('F');
  await dialog.getByLabel('Date de naissance').fill('2008-03-14');
  await dialog.getByLabel('Lieu de naissance').fill('Lubumbashi');
  await dialog.getByLabel('Contact parent').fill('0812345678');
  await dialog.getByLabel('Promotion').selectOption({ label: 'TLM1 — Première année' });
  await dialog.getByRole('button', { name: 'Enregistrer l’inscription' }).click();
  await expect(page.getByRole('heading', { name: 'TSHIAMALA KANYINDA Esther' })).toBeVisible();
  await expect(page.locator('.tag-code').first()).toHaveText(/^TLM1\d{2}$/);
});

test('encaissement réparti et reçu numéroté', async () => {
  await page.getByRole('button', { name: 'Encaisser' }).first().click();
  await expect(page).toHaveURL(/encaisser\?inscription=/);
  // Paiement par mois : on coche le premier mois dû, son montant est celui de l'échéance.
  await page.locator('label.check input[type=checkbox]').first().check();
  await expect(page.getByText(/Aucun mois choisi/)).toHaveCount(0);
  await expect(page.locator('table.data tfoot')).toContainText('55 000');
  await page.getByRole('button', { name: /Valider le paiement/ }).click();
  await expect(page.getByText('Paiement enregistré')).toBeVisible();
  await expect(page.locator('.tag-code', { hasText: /RC-\d{4}-\d{6}/ })).toBeVisible();
});

test('sans mois choisi, le paiement ne part pas', async () => {
  await page.getByRole('button', { name: 'Autre paiement pour cet apprenant' }).click();
  await expect(page.getByText(/Aucun mois choisi/)).toBeVisible();
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

test('ruban : onglets, touches d’accès et commandes', async () => {
  await page.goto('/');
  await expect(page.getByRole('tab', { name: 'Accueil' })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('tab', { name: 'Finances' }).click();
  await expect(page.getByRole('button', { name: 'Encaisser', exact: true })).toBeVisible();
  // Alt seul affiche les touches d’accès ; la lettre ouvre l’onglet, puis la commande.
  await page.keyboard.press('Alt');
  await page.keyboard.up('Alt');
  await expect(page.locator('.rb-keytip').first()).toBeVisible();
  await page.keyboard.press('S');
  await expect(page.getByRole('tab', { name: 'Secrétariat' })).toHaveAttribute('aria-selected', 'true');
  await page.keyboard.press('A');
  await expect(page).toHaveURL(/\/apprenants$/);
  await expect(page.locator('.rb-keytip')).toHaveCount(0);
});

test('ruban : réduction, préférences et vue Fichier', async () => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Réduire le ruban' }).click();
  await expect(page.locator('.ribbon-body')).toHaveCount(0);
  await page.getByRole('button', { name: 'Afficher le ruban' }).click();
  await expect(page.locator('.ribbon-body')).toBeVisible();
  await page.getByRole('tab', { name: 'Affichage' }).click();
  await page.getByRole('button', { name: 'Mode sombre' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.getByRole('button', { name: 'Mode sombre' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.locator('.rb-file').click();
  await expect(page.getByRole('dialog', { name: 'Fichier' })).toBeVisible();
  await page.getByRole('button', { name: 'Données et sauvegardes' }).click();
  await expect(page).toHaveURL(/\/donnees$/);
  await expect(page.getByRole('dialog', { name: 'Fichier' })).toHaveCount(0);
});

test('chaque commande du ruban répond sans erreur', async () => {
  test.setTimeout(240_000);
  await page.goto('/');
  const tabs = ['Accueil', 'Secrétariat', 'Finances', 'Scolarité', 'Administration', 'Affichage', 'Aide'];
  for (const tab of tabs) {
    await page.getByRole('tab', { name: tab, exact: true }).click();
    expect(await page.locator('.rb-cmd').count(), `commandes de l’onglet ${tab}`).toBeGreaterThan(0);
    // Le nombre de commandes peut changer (liste des récents) : on le relit à chaque tour.
    for (let i = 0; i < await page.locator('.rb-cmd').count(); i++) {
      // Le ruban peut avoir été réduit par la commande précédente : on le redéploie.
      if (!(await page.locator('.ribbon-body').count())) await page.getByRole('button', { name: 'Afficher le ruban' }).click();
      await page.getByRole('tab', { name: tab, exact: true }).click();
      const cmd = page.locator('.rb-cmd').nth(i);
      const label = (await cmd.getAttribute('aria-label')) ?? '';
      // Le repliement du ruban est testé à part : il masquerait les commandes suivantes.
      if (label === 'Réduire le ruban') continue;
      await cmd.click();
      await page.waitForTimeout(150);
      // Une boîte ouverte par la commande se ferme avant la suivante.
      if (await page.locator('.overlay').count()) {
        await page.keyboard.press('Escape');
        if (await page.locator('.overlay').count()) {
          await page.locator('.overlay').getByRole('button').filter({ hasText: /Fermer|Terminé|Annuler|Retour/ }).first().click().catch(() => {});
        }
        await expect(page.locator('.overlay'), `fermeture après « ${label} »`).toHaveCount(0);
      }
      await expect(page.locator('.statusbar, .titlebar').first(), `écran après « ${label} »`).toBeVisible();
    }
  }
  await page.goto('/');
});

test('actions du ruban : chaque page ouvre sa boîte de saisie', async () => {
  const cases: [string, RegExp][] = [
    ['/depenses?action=nouvelle', /depenses/],
    ['/caisse?action=arreter', /caisse/],
    ['/discipline?action=incident', /discipline/],
    ['/parametres?onglet=promotions&action=promotion', /parametres/],
    ['/parametres?onglet=annees&action=annee', /parametres/],
    ['/utilisateurs?action=nouveau', /utilisateurs/],
    ['/personnel?action=nouveau', /personnel/],
  ];
  for (const [url, expected] of cases) {
    await page.goto(url);
    await expect(page.getByRole('dialog'), url).toBeVisible();
    await expect(page, `paramètre d’action retiré pour ${url}`).not.toHaveURL(/action=/);
    await expect(page).toHaveURL(expected);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
  }
  await page.goto('/donnees?action=importer');
  await expect(page.locator('#import-v79')).toBeVisible();
  await expect(page).not.toHaveURL(/action=/);
});

test('préférences : taille du texte, densité, animations, quadrillage et persistance', async () => {
  await page.goto('/');
  await page.getByRole('tab', { name: 'Affichage' }).click();
  await page.locator('button.rb-launcher').first().click();
  const dialog = page.getByRole('dialog', { name: 'Préférences d’affichage' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Très grand' }).click();
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--fs').trim())).toBe('1.3');
  await dialog.getByRole('button', { name: 'Compacte' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-density', 'compact');
  await dialog.getByRole('checkbox', { name: 'Animations de l’interface' }).uncheck();
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'reduced');
  await dialog.getByRole('checkbox', { name: 'Quadrillage des tableaux' }).check();
  await expect(page.locator('html')).toHaveAttribute('data-grid', 'on');
  await dialog.getByRole('checkbox', { name: 'Afficher les info-bulles du ruban' }).uncheck();
  await expect(page.locator('html')).toHaveAttribute('data-tips', 'off');
  await dialog.getByRole('button', { name: 'Terminé' }).click();

  // Les réglages survivent au rechargement.
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-density', 'compact');
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'reduced');
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--fs').trim())).toBe('1.3');

  // Retour aux valeurs par défaut.
  await page.getByRole('tab', { name: 'Affichage' }).click();
  await page.locator('button.rb-launcher').first().click();
  await page.getByRole('dialog').getByRole('button', { name: 'Rétablir les valeurs par défaut' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-density', 'confortable');
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'normal');
  await expect(page.locator('html')).toHaveAttribute('data-grid', 'off');
  await page.getByRole('dialog').getByRole('button', { name: 'Terminé' }).click();
});

test('déconnexion puis verrouillage après cinq erreurs', async () => {
  await page.getByRole('button', { name: /Administrateur Démo/ }).click();
  await page.getByRole('button', { name: 'Se déconnecter' }).click();
  await expect(page).toHaveURL(/connexion/);
  for (let i = 1; i <= 4; i++) {
    await page.getByLabel('Identifiant').fill(admin.user);
    await page.getByLabel('Mot de passe').fill('mauvais-mot');
    await page.getByRole('button', { name: 'Se connecter' }).click();
    await expect(page.locator('.signin-error')).toContainText(i === 4 ? 'Dernière tentative' : `Encore ${5 - i} tentatives`);
  }
  await page.getByLabel('Mot de passe').fill('mauvais-mot');
  await page.getByRole('button', { name: 'Se connecter' }).click();
  // Cinquième erreur : formulaire bloqué et compte à rebours de 2 minutes.
  await expect(page.locator('.signin-lock')).toContainText('Accès verrouillé');
  await expect(page.locator('.signin-count')).toHaveText(/^(2:00|1:5\d)$/);
  await expect(page.getByLabel('Mot de passe')).toBeDisabled();
  const first = await page.locator('.signin-count').textContent();
  await expect(page.locator('.signin-count')).not.toHaveText(first ?? '', { timeout: 3000 });
  // Un rechargement ne remet pas le compteur à zéro.
  await page.reload();
  await expect(page.locator('.signin-lock')).toBeVisible();
  // Fin du compte à rebours : l'accès revient sans intervention.
  await page.evaluate(() => {
    const key = 'scolia.connexion.verrou';
    const lock = JSON.parse(localStorage.getItem(key) ?? '{}');
    localStorage.setItem(key, JSON.stringify({ ...lock, until: Date.now() + 1500 }));
  });
  await page.reload();
  await expect(page.locator('.signin-ok')).toContainText('Accès rétabli', { timeout: 5000 });
  await expect(page.getByLabel('Mot de passe')).toBeEnabled();
  await expect(page.getByLabel('Mot de passe')).toBeFocused();
  // Le serveur garde son propre verrou : il renvoie le temps restant et le compteur reprend.
  await page.getByLabel('Identifiant').fill(admin.user);
  await page.getByLabel('Mot de passe').fill(admin.password);
  await page.getByRole('button', { name: 'Se connecter' }).click();
  await expect(page.locator('.signin-count')).toHaveText(/^1:[45]\d$/);
});
