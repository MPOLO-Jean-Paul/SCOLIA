import type { ReactNode } from 'react';
import {
  AlertOctagon, Banknote, Ban, BarChart3, BookOpen, Briefcase, CalendarPlus, ChevronUp, ClipboardCheck, Clock, Database, DatabaseBackup,
  FileDown, FileSpreadsheet, FileText, FilePlus, Grid3x3, GraduationCap, HandCoins, Info, Keyboard, LayoutDashboard, Maximize, Monitor, Moon,
  Pencil, Printer, Receipt, RefreshCw, Rows2, RotateCw, Save, Search, Settings, ShieldCheck, Sparkles, Type, Upload, UserCog, UserPlus, Users,
  Wallet, ZoomIn, ZoomOut, Plus, Lock,
} from 'lucide-react';
import { P } from './session';

export type Tone = 'blue' | 'green' | 'orange' | 'purple' | 'red';
export type Size = 'large' | 'small';

export interface RibbonCommand {
  /** Identifiant : commande globale (traitée par la barre) ou page (reçue par runCommand). */
  id: string;
  label: string;
  /** Description affichée dans l'info-bulle. */
  tip: string;
  icon: ReactNode;
  /** Touche d'accès : après Alt, lettre affichée sur le bouton. Unique dans son onglet. */
  key: string;
  size: Size;
  tone?: Tone;
  /** Navigation vers une page. */
  to?: string;
  /** Permission requise pour afficher la commande. */
  perm?: string;
  shortcut?: string;
  /** Bouton à deux états (affiché enfoncé quand l'état est actif). */
  toggle?: boolean;
}

export interface RibbonGroup {
  title: string;
  commands: RibbonCommand[];
  /** Lanceur de boîte de dialogue, petite flèche dans l'angle du groupe. */
  launcher?: { id: string; tip: string };
}

export interface RibbonTab {
  id: string;
  label: string;
  /** Touche d'accès de l'onglet. */
  key: string;
  groups: RibbonGroup[];
  /** Onglet contextuel : n'apparaît que sur certaines pages, avec un titre coloré. */
  context?: Tone;
  contextLabel?: string;
}

export interface RibbonContext {
  pathname: string;
  recents: { to: string; label: string }[];
  can: (permission: string) => boolean;
}

const large = (c: Omit<RibbonCommand, 'size'>): RibbonCommand => ({ size: 'large', ...c });
const small = (c: Omit<RibbonCommand, 'size'>): RibbonCommand => ({ size: 'small', ...c });

const RECEIPT_PAGE = /^\/paiements\/recus\/[^/]+$/;
const STUDENT_PAGE = /^\/apprenants\/[^/]+$/;

/** Construit les onglets visibles pour l'utilisateur et la page courante. */
export function buildTabs({ pathname, recents, can }: RibbonContext): RibbonTab[] {
  const tabs: RibbonTab[] = [
    {
      id: 'accueil', label: 'Accueil', key: 'A', groups: [
        {
          title: 'Rechercher', commands: [
            large({ id: 'rechercher', label: 'Rechercher', tip: 'Recherche globale : apprenants, reçus, pages et commandes.', icon: <Search />, key: 'R', shortcut: 'Ctrl+K', tone: 'blue' }),
          ],
        },
        {
          title: 'Page', commands: [
            large({ id: 'actualiser', label: 'Actualiser', tip: 'Recharge les données affichées depuis le serveur.', icon: <RefreshCw />, key: 'A', shortcut: 'F5', tone: 'blue' }),
            large({ id: 'imprimer', label: 'Imprimer', tip: 'Imprime la page affichée.', icon: <Printer />, key: 'I', shortcut: 'Ctrl+P', tone: 'blue' }),
            small({ id: 'plein-ecran', label: 'Plein écran', tip: 'Affiche SCOLIA sans bordures de fenêtre. Échap pour revenir.', icon: <Maximize />, key: 'P', toggle: true }),
          ],
        },
        {
          title: 'Récents', commands: recents.slice(0, 6).map((r, i) => small({
            id: `recent-${r.to}`, label: r.label, tip: `Ouvrir ${r.label}.`, icon: <Clock />, key: String(i + 1), to: r.to,
          })),
        },
      ],
    },
    {
      id: 'secretariat', label: 'Secrétariat', key: 'S', groups: [
        {
          title: 'Apprenants', commands: [
            large({ id: 'apprenants', label: 'Apprenants', tip: 'Liste, recherche et fiches des apprenants.', icon: <Users />, key: 'A', to: '/apprenants', perm: P.studentsRead, shortcut: 'Alt+2', tone: 'blue' }),
            large({ id: 'nouvelle-inscription', label: 'Nouvelle inscription', tip: 'Inscrit un apprenant. Le matricule est attribué automatiquement.', icon: <UserPlus />, key: 'N', to: '/apprenants?nouveau=1', perm: P.studentsWrite, tone: 'green' }),
            small({ id: 'reinscriptions', label: 'Réinscriptions', tip: 'Reconduit les apprenants vers la promotion suivante.', icon: <GraduationCap />, key: 'R', to: '/reinscriptions', perm: P.studentsWrite }),
          ],
        },
      ],
    },
    {
      id: 'finances', label: 'Finances', key: 'M', groups: [
        {
          title: 'Encaissement', commands: [
            large({ id: 'encaisser', label: 'Encaisser', tip: 'Encaisse un versement : il est réparti sur les échéances et numéroté.', icon: <HandCoins />, key: 'E', to: '/encaisser', perm: P.paymentsCreate, shortcut: 'F2', tone: 'green' }),
            large({ id: 'journal-recus', label: 'Journal des reçus', tip: 'Tous les reçus, leur statut et les annulations motivées.', icon: <Receipt />, key: 'J', to: '/paiements', perm: P.paymentsRead }),
          ],
        },
        {
          title: 'Suivi', commands: [
            large({ id: 'recouvrement', label: 'Recouvrement', tip: 'Retards classés par ancienneté, avec rappels WhatsApp et SMS.', icon: <BarChart3 />, key: 'R', to: '/recouvrement', perm: P.reportsRead, shortcut: 'Alt+4', tone: 'orange' }),
            small({ id: 'prises-en-charge', label: 'Prises en charge', tip: 'Bourses, sponsors et exonérations de l’année.', icon: <ShieldCheck />, key: 'P', to: '/prises-en-charge', perm: P.paymentsRead }),
          ],
        },
        {
          title: 'Caisse', commands: [
            large({ id: 'caisse', label: 'Caisse', tip: 'Mouvements de la période et situation de la caisse.', icon: <Banknote />, key: 'C', to: '/caisse', perm: P.paymentsRead, tone: 'green' }),
            large({ id: 'depenses', label: 'Dépenses', tip: 'Sorties de caisse et de banque, numérotées et justifiées.', icon: <Wallet />, key: 'Z', to: '/depenses', perm: P.paymentsRead, tone: 'red' }),
            small({ id: 'nouvelle-depense', label: 'Nouvelle dépense', tip: 'Ouvre le formulaire d’une nouvelle dépense.', icon: <Plus />, key: 'N', to: '/depenses?action=nouvelle', perm: P.expensesWrite }),
            small({ id: 'arreter-caisse', label: 'Arrêter la caisse', tip: 'Compte les espèces et verrouille la période.', icon: <Lock />, key: 'A', to: '/caisse?action=arreter', perm: P.cashClose }),
          ],
        },
      ],
    },
    {
      id: 'scolarite', label: 'Scolarité', key: 'C', groups: [
        {
          title: 'Cours', commands: [
            large({ id: 'notes', label: 'Cours et notes', tip: 'Cours, évaluations, saisie des notes, moyennes et bulletins.', icon: <BookOpen />, key: 'N', to: '/notes', perm: P.academicsRead, tone: 'purple' }),
            large({ id: 'nouveau-cours', label: 'Nouveau cours', tip: 'Crée un cours dans la promotion choisie.', icon: <FilePlus />, key: 'U', to: '/notes?action=cours', perm: P.academicsWrite, tone: 'purple' }),
            small({ id: 'nouvelle-evaluation', label: 'Nouvelle évaluation', tip: 'Crée une évaluation pour un cours.', icon: <ClipboardCheck />, key: 'E', to: '/notes?action=evaluation', perm: P.academicsWrite }),
          ],
        },
        {
          title: 'Vie de classe', commands: [
            large({ id: 'presences', label: 'Présences', tip: 'Appel quotidien par promotion et taux d’assiduité.', icon: <ClipboardCheck />, key: 'P', to: '/presences', perm: P.academicsRead, tone: 'blue' }),
            large({ id: 'discipline', label: 'Discipline', tip: 'Incidents, sanctions et suivi du comportement.', icon: <AlertOctagon />, key: 'D', to: '/discipline', perm: P.academicsRead, tone: 'red' }),
            small({ id: 'nouvel-incident', label: 'Nouvel incident', tip: 'Signale un incident de discipline.', icon: <Plus />, key: 'I', to: '/discipline?action=incident', perm: P.academicsWrite }),
          ],
        },
      ],
    },
    {
      id: 'administration', label: 'Administration', key: 'D', groups: [
        {
          title: 'Personnel', commands: [
            large({ id: 'personnel', label: 'Personnel et paie', tip: 'Dossiers du personnel, bulletins mensuels et paiement des salaires.', icon: <Briefcase />, key: 'P', to: '/personnel', perm: P.staffManage }),
            small({ id: 'nouveau-membre', label: 'Nouveau membre', tip: 'Ajoute un membre du personnel.', icon: <UserPlus />, key: 'M', to: '/personnel?action=nouveau', perm: P.staffManage }),
          ],
        },
        {
          title: 'Établissement', commands: [
            large({ id: 'parametres', label: 'Paramètres', tip: 'Identité, années scolaires, promotions et grilles de frais.', icon: <Settings />, key: 'A', to: '/parametres', perm: P.settingsWrite }),
            large({ id: 'utilisateurs', label: 'Utilisateurs', tip: 'Comptes nominatifs, rôles et mots de passe provisoires.', icon: <UserCog />, key: 'U', to: '/utilisateurs', perm: P.usersManage }),
            small({ id: 'nouvelle-promotion', label: 'Nouvelle promotion', tip: 'Crée une promotion avec ses frais.', icon: <Plus />, key: 'R', to: '/parametres?onglet=promotions&action=promotion', perm: P.settingsWrite }),
            small({ id: 'nouvelle-annee', label: 'Nouvelle année', tip: 'Prépare une nouvelle année scolaire.', icon: <CalendarPlus />, key: 'Y', to: '/parametres?onglet=annees&action=annee', perm: P.settingsWrite }),
            small({ id: 'nouvel-utilisateur', label: 'Nouvel utilisateur', tip: 'Crée un compte nominatif.', icon: <UserPlus />, key: 'N', to: '/utilisateurs?action=nouveau', perm: P.usersManage }),
          ],
        },
      ],
    },
    {
      id: 'donnees', label: 'Données', key: 'O', groups: [
        {
          title: 'Pilotage', commands: [
            large({ id: 'tableau-de-bord', label: 'Tableau de bord', tip: 'Journée en cours, indicateurs de l’année, recettes et retards de paiement.', icon: <LayoutDashboard />, key: 'B', to: '/', shortcut: 'Alt+1', tone: 'blue' }),
          ],
        },
        {
          title: 'Exports rapides', commands: [
            small({ id: 'export-situation-pdf', label: 'Situation (PDF)', tip: 'Situation financière de chaque promotion, en PDF.', icon: <FileDown />, key: 'S', perm: P.reportsRead }),
            small({ id: 'export-apprenants-xlsx', label: 'Apprenants (Excel)', tip: 'Liste générale des apprenants de l’année avec leur solde.', icon: <FileSpreadsheet />, key: 'L', perm: P.reportsRead }),
            small({ id: 'export-recus-xlsx', label: 'Reçus (Excel)', tip: 'Exporte tous les reçus dans un classeur Excel.', icon: <FileSpreadsheet />, key: 'X', perm: P.paymentsRead }),
            small({ id: 'export-recus-pdf', label: 'Reçus (PDF)', tip: 'Exporte la liste des reçus au format PDF.', icon: <FileDown />, key: 'F', perm: P.paymentsRead }),
            small({ id: 'export-debiteurs-xlsx', label: 'Débiteurs (Excel)', tip: 'Exporte la liste des retards de paiement dans Excel.', icon: <FileSpreadsheet />, key: 'E', perm: P.reportsRead }),
            small({ id: 'export-debiteurs-pdf', label: 'Débiteurs (PDF)', tip: 'Exporte la liste des retards de paiement en PDF.', icon: <FileDown />, key: 'P', perm: P.reportsRead }),
            small({ id: 'export-depenses-xlsx', label: 'Dépenses (Excel)', tip: 'Registre des dépenses dans un classeur Excel.', icon: <FileSpreadsheet />, key: 'R', perm: P.paymentsRead }),
          ],
        },
        {
          title: 'Sauvegardes', commands: [
            large({ id: 'donnees', label: 'Sauvegardes', tip: 'Sauvegardes vérifiées, téléchargement et restauration.', icon: <DatabaseBackup />, key: 'D', to: '/donnees', perm: P.backupManage, tone: 'green' }),
            small({ id: 'sauvegarder', label: 'Sauvegarder', tip: 'Crée tout de suite une sauvegarde vérifiée.', icon: <Save />, key: 'G', perm: P.backupManage }),
            small({ id: 'importer-v79', label: 'Importer SCOLIA V79', tip: 'Reprend les données d’un classeur Excel de l’ancienne version.', icon: <Upload />, key: 'I', to: '/donnees?action=importer', perm: P.backupManage }),
          ],
        },
        {
          title: 'Contrôle', commands: [
            large({ id: 'audit', label: 'Journal d’audit', tip: 'Qui a fait quoi, et vérification de la chaîne d’empreintes.', icon: <ShieldCheck />, key: 'J', to: '/audit', perm: P.auditRead }),
            small({ id: 'diag-base', label: 'Vérifier la base', tip: 'Contrôle d’intégrité de la base de données.', icon: <Database />, key: 'A', perm: P.backupManage }),
            small({ id: 'diag-audit', label: 'Vérifier le journal', tip: 'Contrôle de la chaîne d’empreintes du journal d’audit.', icon: <ShieldCheck />, key: 'K', perm: P.auditRead }),
          ],
        },
      ],
    },
    {
      id: 'affichage', label: 'Affichage', key: 'W', groups: [
        {
          title: 'Apparence', commands: [
            large({ id: 'theme', label: 'Mode sombre', tip: 'Bascule entre le thème clair et le thème sombre.', icon: <Moon />, key: 'M', toggle: true, tone: 'blue' }),
            small({ id: 'taille-texte', label: 'Taille du texte', tip: 'Fait défiler les tailles : petite, normale, grande, très grande.', icon: <Type />, key: 'T' }),
            small({ id: 'densite', label: 'Densité compacte', tip: 'Lignes et boutons plus serrés, pour afficher davantage de données.', icon: <Rows2 />, key: 'D', toggle: true }),
            small({ id: 'barre-etat', label: 'Barre d’état', tip: 'Affiche la barre du bas : sauvegarde, année et zoom.', icon: <Monitor />, key: 'B', toggle: true }),
          ],
          launcher: { id: 'preferences', tip: 'Ouvre toutes les préférences d’affichage.' },
        },
        {
          title: 'Affichage', commands: [
            small({ id: 'quadrillage', label: 'Quadrillage', tip: 'Affiche les lignes entre les colonnes des tableaux.', icon: <Grid3x3 />, key: 'Q', toggle: true }),
            small({ id: 'info-bulles', label: 'Info-bulles', tip: 'Affiche la description des commandes au survol.', icon: <Info />, key: 'I', toggle: true }),
            small({ id: 'animations', label: 'Animations', tip: 'Désactive les transitions et les animations de l’interface.', icon: <Sparkles />, key: 'A', toggle: true }),
            small({ id: 'ruban', label: 'Réduire le ruban', tip: 'Masque les commandes et ne garde que les onglets.', icon: <ChevronUp />, key: 'R', toggle: true, shortcut: 'Ctrl+F1' }),
          ],
        },
        {
          title: 'Zoom', commands: [
            small({ id: 'zoom-moins', label: 'Zoom arrière', tip: 'Réduit la taille de l’interface.', icon: <ZoomOut />, key: 'O' }),
            small({ id: 'zoom-plus', label: 'Zoom avant', tip: 'Agrandit la taille de l’interface.', icon: <ZoomIn />, key: 'Z' }),
            small({ id: 'zoom-reset', label: 'Zoom 100 %', tip: 'Rétablit la taille normale.', icon: <RotateCw />, key: '1' }),
          ],
        },
      ],
    },
    {
      id: 'aide', label: 'Aide', key: 'Y', groups: [
        {
          title: 'Aide', commands: [
            large({ id: 'raccourcis', label: 'Raccourcis clavier', tip: 'Liste des raccourcis et des touches d’accès du ruban.', icon: <Keyboard />, key: 'R', tone: 'blue' }),
            large({ id: 'apropos', label: 'À propos', tip: 'Version de SCOLIA Pro et conditions d’utilisation.', icon: <Info />, key: 'A' }),
          ],
        },
      ],
    },
  ];

  if (RECEIPT_PAGE.test(pathname)) {
    tabs.push({
      id: 'outils-recu', label: 'Reçu', key: 'U', context: 'orange', contextLabel: 'Outils du reçu', groups: [
        {
          title: 'Reçu', commands: [
            large({ id: 'imprimer-recu', label: 'Imprimer', tip: 'Ouvre le reçu au format PDF A5, avec son QR de vérification.', icon: <Printer />, key: 'I', tone: 'blue' }),
            large({ id: 'annuler-recu', label: 'Annuler le reçu', tip: 'Annule le reçu avec un motif. Un reçu n’est jamais supprimé.', icon: <Ban />, key: 'A', perm: P.paymentsCancel, tone: 'red' }),
            small({ id: 'verifier-recu', label: 'Vérifier en ligne', tip: 'Ouvre la page publique de vérification du reçu.', icon: <ShieldCheck />, key: 'V' }),
            small({ id: 'journal-recus-retour', label: 'Journal des reçus', tip: 'Retour à la liste des reçus.', icon: <Receipt />, key: 'J', to: '/paiements' }),
          ],
        },
      ],
    });
  }

  if (STUDENT_PAGE.test(pathname)) {
    tabs.push({
      id: 'outils-apprenant', label: 'Apprenant', key: 'U', context: 'purple', contextLabel: 'Outils de l’apprenant', groups: [
        {
          title: 'Encaissement', commands: [
            large({ id: 'encaisser-eleve', label: 'Encaisser', tip: 'Encaisse un versement pour cet apprenant.', icon: <HandCoins />, key: 'E', perm: P.paymentsCreate, tone: 'green' }),
          ],
        },
        {
          title: 'Dossier', commands: [
            large({ id: 'modifier-eleve', label: 'Modifier', tip: 'Modifie l’identité de l’apprenant.', icon: <Pencil />, key: 'M', perm: P.studentsWrite, tone: 'blue' }),
            small({ id: 'releve-eleve', label: 'Relevé de compte', tip: 'Relevé PDF des frais et des paiements.', icon: <FileText />, key: 'R', perm: P.paymentsRead }),
            small({ id: 'liste-apprenants', label: 'Liste des apprenants', tip: 'Retour à la liste.', icon: <Users />, key: 'L', to: '/apprenants' }),
          ],
        },
      ],
    });
  }

  return tabs
    .map((tab) => ({
      ...tab,
      groups: tab.groups
        .map((group) => ({ ...group, commands: group.commands.filter((c) => !c.perm || can(c.perm)) }))
        .filter((group) => group.commands.length > 0),
    }))
    .filter((tab) => tab.groups.length > 0);
}

/** Toutes les commandes de toutes les pages, sans filtre de droits ni de contexte. */
export function allCommands(): RibbonCommand[] {
  return buildTabs({ pathname: '', recents: [], can: () => true }).flatMap((tab) => tab.groups.flatMap((g) => g.commands));
}

/** Nom du module (onglet du ruban) auquel appartient une page : « Secrétariat » pour /apprenants/:id. */
export function moduleOf(pathname: string): string | undefined {
  for (const tab of buildTabs({ pathname: '', recents: [], can: () => true })) {
    const owns = tab.groups.some((g) => g.commands.some((c) => {
      if (!c.to) return false;
      return c.to === '/' ? pathname === '/' : pathname === c.to || pathname.startsWith(`${c.to}/`);
    }));
    if (owns) return tab.label;
  }
  return undefined;
}
