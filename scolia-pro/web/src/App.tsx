import { lazy, Suspense, type ReactNode } from 'react';
import { Link, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { P, useSession } from './lib/session';
import { accountHome } from './lib/access';
import { Layout } from './components/Layout';
import { Card, Loading, PageHead } from './components/ui';
import { StartupScreen } from './components/StartupScreen';
import { LoginPage, SetupPage } from './pages/Auth';

// Chaque écran est un fragment chargé à la demande : le premier affichage ne télécharge que ce qui est nécessaire.
const page = <K extends string>(load: () => Promise<Record<K, React.ComponentType>>, name: K) =>
  lazy(() => load().then((m) => ({ default: m[name] })));
const DashboardPage = page(() => import('./pages/Dashboard'), 'DashboardPage');
const PromoterDashboardPage = page(() => import('./pages/PromoterDashboard'), 'PromoterDashboardPage');
const StudentsPage = page(() => import('./pages/Students'), 'StudentsPage');
const StudentDetailPage = page(() => import('./pages/StudentDetail'), 'StudentDetailPage');
const CashierPage = page(() => import('./pages/Cashier'), 'CashierPage');
const ReceiptsPage = page(() => import('./pages/Finance'), 'ReceiptsPage');
const ReceiptDetailPage = page(() => import('./pages/Finance'), 'ReceiptDetailPage');
const DebtorsPage = page(() => import('./pages/Finance'), 'DebtorsPage');
const SponsorshipsPage = page(() => import('./pages/Finance'), 'SponsorshipsPage');
const ExpensesPage = page(() => import('./pages/Finance'), 'ExpensesPage');
const CashPage = page(() => import('./pages/Finance'), 'CashPage');
const GradesPage = page(() => import('./pages/Academics'), 'GradesPage');
const AttendancePage = page(() => import('./pages/Academics'), 'AttendancePage');
const DisciplinePage = page(() => import('./pages/Academics'), 'DisciplinePage');
const StaffPage = page(() => import('./pages/Admin'), 'StaffPage');
const SettingsPage = page(() => import('./pages/Admin'), 'SettingsPage');
const UsersPage = page(() => import('./pages/Admin'), 'UsersPage');
const DataPage = page(() => import('./pages/Admin'), 'DataPage');
const AuditPage = page(() => import('./pages/Admin'), 'AuditPage');
const ProfilePage = page(() => import('./pages/Misc'), 'ProfilePage');
const ReEnrollmentPage = page(() => import('./pages/Misc'), 'ReEnrollmentPage');
const VerifyPage = page(() => import('./pages/Misc'), 'VerifyPage');

/** Expliquer une route impossible plutôt que renvoyer silencieusement à l'accueil. */
function NotAvailable({ title, reason }: { title: string; reason: string }) {
  const { session } = useSession();
  return <div className="stack">
    <PageHead title={title} subtitle={reason} />
    <Card><p>Vous pouvez revenir à votre espace ou sélectionner une fonctionnalité dans le menu.</p>
      <Link className="btn primary" to={accountHome(session?.permissions ?? [])}>Revenir à mon espace</Link>
    </Card>
  </div>;
}

function RequirePermission({ right, children }: { right: string; children: ReactNode }) {
  const { can } = useSession();
  return can(right) ? <>{children}</> : <NotAvailable title="Accès limité" reason="Votre compte n'a pas l'autorisation d'ouvrir cette fonctionnalité." />;
}

function Landing() {
  const { session } = useSession();
  const home = accountHome(session?.permissions ?? []);
  return home === '/' ? <DashboardPage /> : <Navigate to={home} replace />;
}

function Protected() {
  const { session, loading, configured } = useSession();
  const location = useLocation();
  if (loading) return <StartupScreen />;
  if (configured === false) return <Navigate to="/installation" replace />;
  if (!session) return <Navigate to="/connexion" replace state={{ from: location.pathname }} />;
  if (session.mustChangePassword && location.pathname !== '/profil') return <Navigate to="/profil" replace />;
  return <Layout />;
}

export function App() {
  const { session, configured, loading } = useSession();
  return (
    <Routes>
      <Route path="/verifier/:code" element={<Suspense fallback={<Loading rows={3} />}><VerifyPage /></Suspense>} />
      <Route path="/installation" element={loading ? <StartupScreen /> : configured ? <Navigate to="/" replace /> : <SetupPage />} />
      <Route path="/connexion" element={loading ? <StartupScreen /> : session ? <Navigate to="/" replace /> : configured === false ? <Navigate to="/installation" replace /> : <LoginPage />} />
      <Route element={<Protected />}>
        <Route index element={<Landing />} />
        <Route path="pilotage" element={<RequirePermission right={P.pilotageRead}><PromoterDashboardPage /></RequirePermission>} />
        <Route path="apprenants" element={<RequirePermission right={P.studentsRead}><StudentsPage /></RequirePermission>} />
        <Route path="apprenants/:id" element={<RequirePermission right={P.studentsRead}><StudentDetailPage /></RequirePermission>} />
        <Route path="reinscriptions" element={<RequirePermission right={P.studentsWrite}><ReEnrollmentPage /></RequirePermission>} />
        <Route path="encaisser" element={<RequirePermission right={P.paymentsCreate}><CashierPage /></RequirePermission>} />
        <Route path="paiements" element={<RequirePermission right={P.paymentsRead}><ReceiptsPage /></RequirePermission>} />
        <Route path="paiements/recus/:id" element={<RequirePermission right={P.paymentsRead}><ReceiptDetailPage /></RequirePermission>} />
        <Route path="recouvrement" element={<RequirePermission right={P.reportsRead}><DebtorsPage /></RequirePermission>} />
        <Route path="prises-en-charge" element={<RequirePermission right={P.paymentsRead}><SponsorshipsPage /></RequirePermission>} />
        <Route path="depenses" element={<RequirePermission right={P.paymentsRead}><ExpensesPage /></RequirePermission>} />
        <Route path="caisse" element={<RequirePermission right={P.paymentsRead}><CashPage /></RequirePermission>} />
        <Route path="notes" element={<RequirePermission right={P.academicsRead}><GradesPage /></RequirePermission>} />
        <Route path="presences" element={<RequirePermission right={P.academicsRead}><AttendancePage /></RequirePermission>} />
        <Route path="discipline" element={<RequirePermission right={P.academicsRead}><DisciplinePage /></RequirePermission>} />
        <Route path="personnel" element={<RequirePermission right={P.staffManage}><StaffPage /></RequirePermission>} />
        <Route path="parametres" element={<RequirePermission right={P.settingsWrite}><SettingsPage /></RequirePermission>} />
        <Route path="utilisateurs" element={<RequirePermission right={P.usersManage}><UsersPage /></RequirePermission>} />
        <Route path="donnees" element={<RequirePermission right={P.backupManage}><DataPage /></RequirePermission>} />
        <Route path="audit" element={<RequirePermission right={P.auditRead}><AuditPage /></RequirePermission>} />
        <Route path="profil" element={<ProfilePage />} />
        <Route path="*" element={<NotAvailable title="Page introuvable" reason="Ce lien n’existe plus ou contient une erreur." />} />
      </Route>
    </Routes>
  );
}
