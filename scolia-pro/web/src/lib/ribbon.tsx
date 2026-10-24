import type { ReactNode } from 'react';
import {
  AlertOctagon, Banknote, Ban, BarChart3, BookOpen, Briefcase, ChevronUp, ClipboardCheck, Clock, Database, DatabaseBackup, FileSpreadsheet,
  FileText, GraduationCap, HandCoins, Info, Keyboard, LayoutDashboard, Monitor, Moon, Pencil, Printer, Receipt, RefreshCw, Rows2, RotateCw,
  Save, Search, Settings, ShieldCheck, UserCog, UserPlus, Users, Wallet, ZoomIn, ZoomOut,
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
          title: 'Tableau de bord', commands: [
            large({ id: 'tableau-de-bord', label: 'Tableau de bord', tip: 'Indicateurs du jour, recettes et retards de paiement.', icon: <LayoutDashboard />, key: 'B', to: '/', shortcut: 'Alt+1', tone: 'blue' }),
            large({ id: 'actualiser', label: 'Actualiser', tip: 'Recharge les données affichées depuis le serveur.', icon: <RefreshCw />, key: 'A', shortcut: 'F5', tone: 'blue' }),
          ],
        },
        {
          title: 'Rechercher', commands: [
            large({ id: 'rechercher', label: 'Rechercher', tip: 'Recherche globale : apprenants, reçus, pages et commandes.', icon: <Search />, key: 'R', shortcut: 'Ctrl+K', tone: 'blue' }),
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
            large({ id: 'caisse', label: 'Caisse', tip: 'Mouvements de la période et arrêté de caisse.', icon: <Banknote />, key: 'C', to: '/caisse', perm: P.paymentsRead, tone: 'green' }),
            large({ id: 'depenses', label: 'Dépenses', tip: 'Sorties de caisse et de banque, numérotées et justifiées.', icon: <Wallet />, key: 'D', to: '/depenses', perm: P.paymentsRead, tone: 'red' }),
          ],
        },
      ],
    },
    {
      id: 'scolarite', label: 'Scolarité', key: 'C', groups: [
        {
          title: 'Cours', commands: [
            large({ id: 'notes', label: 'Cours et notes', tip: 'Cours, évaluations, saisie des notes, moyennes et bulletins.', icon: <BookOpen />, key: 'N', to: '/notes', perm: P.academicsRead, tone: 'purple' }),
          ],
        },
        {
          title: 'Vie de classe', commands: [
            large({ id: 'presences', label: 'Présences', tip: 'Appel quotidien par promotion et taux d’assiduité.', icon: <ClipboardCheck />, key: 'P', to: '/presences', perm: P.academicsRead, tone: 'blue' }),
            large({ id: 'discipline', label: 'Discipline', tip: 'Incidents, sanctions et suivi du comportement.', icon: <AlertOctagon />, key: 'D', to: '/discipline', perm: P.academicsRead, tone: 'red' }),
          ],
        },
      ],
    },
    {
      id: 'administration', label: 'Administration', key: 'D', groups: [
        {
          title: 'Personnel', commands: [
            large({ id: 'personnel', label: 'Personnel et paie', tip: 'Dossiers du personnel, bulletins mensuels et paiement des salaires.', icon: <Briefcase />, key: 'P', to: '/personnel', perm: P.staffManage }),
          ],
        },
        {
          title: 'Établissement', commands: [
            large({ id: 'parametres', label: 'Paramètres', tip: 'Identité, années scolaires, promotions et grilles de frais.', icon: <Settings />, key: 'A', to: '/parametres', perm: P.settingsWrite }),
            large({ id: 'utilisateurs', label: 'Utilisateurs', tip: 'Comptes nominatifs, rôles et mots de passe provisoires.', icon: <UserCog />, key: 'U', to: '/utilisateurs', perm: P.usersManage }),
          ],
        },
        {
          title: 'Données', commands: [
            large({ id: 'donnees', label: 'Données et sauvegardes', tip: 'Sauvegardes, restauration et import du classeur V79.', icon: <DatabaseBackup />, key: 'D', to: '/donnees', perm: P.backupManage, tone: 'green' }),
            large({ id: 'audit', label: 'Journal d’audit', tip: 'Qui a fait quoi, et vérification de la chaîne d’empreintes.', icon: <FileSpreadsheet />, key: 'J', to: '/audit', perm: P.auditRead }),
            small({ id: 'sauvegarder', label: 'Sauvegarder', tip: 'Crée tout de suite une sauvegarde vérifiée.', icon: <Save />, key: 'S', perm: P.backupManage }),
          ],
        },
      ],
    },
    {
      id: 'affichage', label: 'Affichage', key: 'W', groups: [
        {
          title: 'Apparence', commands: [
            large({ id: 'theme', label: 'Mode sombre', tip: 'Bascule entre le thème clair et le thème sombre.', icon: <Moon />, key: 'M', toggle: true, tone: 'blue' }),
            small({ id: 'densite', label: 'Densité compacte', tip: 'Lignes et boutons plus serrés, pour afficher davantage de données.', icon: <Rows2 />, key: 'D', toggle: true }),
            small({ id: 'barre-etat', label: 'Barre d’état', tip: 'Affiche la barre du bas : sauvegarde, année et zoom.', icon: <Monitor />, key: 'B', toggle: true }),
          ],
          launcher: { id: 'preferences', tip: 'Ouvre toutes les préférences d’affichage.' },
        },
        {
          title: 'Ruban', commands: [
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
        {
          title: 'Diagnostic', commands: [
            small({ id: 'diag-base', label: 'Vérifier la base', tip: 'Contrôle d’intégrité de la base de données.', icon: <Database />, key: 'B', perm: P.backupManage }),
            small({ id: 'diag-audit', label: 'Vérifier le journal', tip: 'Contrôle de la chaîne d’empreintes du journal d’audit.', icon: <ShieldCheck />, key: 'J', perm: P.auditRead }),
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
