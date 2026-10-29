import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { useSession } from './lib/session';
import { Layout } from './components/Layout';
import { Loading } from './components/ui';
import { StartupScreen } from './components/StartupScreen';
import { LoginPage, SetupPage } from './pages/Auth';

// Chaque écran est un fragment chargé à la demande : le premier affichage ne télécharge que ce qui est nécessaire.
const page = <K extends string>(load: () => Promise<Record<K, React.ComponentType>>, name: K) =>
  lazy(() => load().then((m) => ({ default: m[name] })));
const DashboardPage = page(() => import('./pages/Dashboard'), 'DashboardPage');
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
        <Route index element={<DashboardPage />} />
        <Route path="apprenants" element={<StudentsPage />} />
        <Route path="apprenants/:id" element={<StudentDetailPage />} />
        <Route path="reinscriptions" element={<ReEnrollmentPage />} />
        <Route path="encaisser" element={<CashierPage />} />
        <Route path="paiements" element={<ReceiptsPage />} />
        <Route path="paiements/recus/:id" element={<ReceiptDetailPage />} />
        <Route path="recouvrement" element={<DebtorsPage />} />
        <Route path="prises-en-charge" element={<SponsorshipsPage />} />
        <Route path="depenses" element={<ExpensesPage />} />
        <Route path="caisse" element={<CashPage />} />
        <Route path="notes" element={<GradesPage />} />
        <Route path="presences" element={<AttendancePage />} />
        <Route path="discipline" element={<DisciplinePage />} />
        <Route path="personnel" element={<StaffPage />} />
        <Route path="parametres" element={<SettingsPage />} />
        <Route path="utilisateurs" element={<UsersPage />} />
        <Route path="donnees" element={<DataPage />} />
        <Route path="audit" element={<AuditPage />} />
        <Route path="profil" element={<ProfilePage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
