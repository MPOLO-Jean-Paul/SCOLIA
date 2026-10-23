import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { useSession } from './lib/session';
import { Layout } from './components/Layout';
import { Loading } from './components/ui';
import { LoginPage, SetupPage } from './pages/Auth';
import { DashboardPage } from './pages/Dashboard';
import { StudentsPage } from './pages/Students';
import { StudentDetailPage } from './pages/StudentDetail';
import { CashierPage } from './pages/Cashier';
import { CashPage, DebtorsPage, ExpensesPage, ReceiptDetailPage, ReceiptsPage, SponsorshipsPage } from './pages/Finance';
import { AttendancePage, DisciplinePage, GradesPage } from './pages/Academics';
import { AuditPage, DataPage, SettingsPage, StaffPage, UsersPage } from './pages/Admin';
import { ProfilePage, ReEnrollmentPage, VerifyPage } from './pages/Misc';

function Protected() {
  const { session, loading, configured } = useSession();
  const location = useLocation();
  if (loading) return <div style={{ padding: 40 }}><Loading rows={6} /></div>;
  if (configured === false) return <Navigate to="/installation" replace />;
  if (!session) return <Navigate to="/connexion" replace state={{ from: location.pathname }} />;
  if (session.mustChangePassword && location.pathname !== '/profil') return <Navigate to="/profil" replace />;
  return <Layout />;
}

export function App() {
  const { session, configured, loading } = useSession();
  return (
    <Routes>
      <Route path="/verifier/:code" element={<VerifyPage />} />
      <Route path="/installation" element={loading ? null : configured ? <Navigate to="/" replace /> : <SetupPage />} />
      <Route path="/connexion" element={session ? <Navigate to="/" replace /> : configured === false ? <Navigate to="/installation" replace /> : <LoginPage />} />
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
