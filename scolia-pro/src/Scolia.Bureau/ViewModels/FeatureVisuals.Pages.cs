using System.Collections.ObjectModel;
using Material.Icons;

namespace Scolia.Bureau.ViewModels;

public static partial class FeatureVisuals
{
    /// <summary>Identité de chaque écran, commune aux menus, cartes et titres sur tous les appareils.</summary>
    public static IReadOnlyDictionary<Type, MaterialIconKind> PageIcons { get; } =
        new ReadOnlyDictionary<Type, MaterialIconKind>(new Dictionary<Type, MaterialIconKind>
        {
            [typeof(HomePage)] = MaterialIconKind.Home,
            [typeof(StartPage)] = MaterialIconKind.ViewGrid,
            [typeof(PromoterDashboardPage)] = MaterialIconKind.ViewDashboardOutline,
            [typeof(DashboardPage)] = MaterialIconKind.ChartBox,
            [typeof(ReportsPage)] = MaterialIconKind.FileChart,
            [typeof(StudentsPage)] = MaterialIconKind.AccountGroup,
            [typeof(StudentAccountsPage)] = MaterialIconKind.AccountKeyOutline,
            [typeof(ReEnrollmentPage)] = MaterialIconKind.CalendarSync,
            [typeof(CardsPage)] = MaterialIconKind.CardAccountDetails,
            [typeof(CashierPage)] = MaterialIconKind.HandCoin,
            [typeof(BatchPaymentPage)] = MaterialIconKind.FormatListChecks,
            [typeof(ReceiptsPage)] = MaterialIconKind.ReceiptText,
            [typeof(MonthlyPage)] = MaterialIconKind.CalendarCheck,
            [typeof(DebtorsPage)] = MaterialIconKind.ChartBar,
            [typeof(CashPage)] = MaterialIconKind.CashMultiple,
            [typeof(ExpensesPage)] = MaterialIconKind.Wallet,
            [typeof(SponsorshipsPage)] = MaterialIconKind.HandHeart,
            [typeof(GradesPage)] = MaterialIconKind.BookOpenVariant,
            [typeof(ResultsPage)] = MaterialIconKind.Certificate,
            [typeof(StudentResultsPage)] = MaterialIconKind.CertificateOutline,
            [typeof(EpstResultsPage)] = MaterialIconKind.FileDocumentOutline,
            [typeof(EpstDeliberationPage)] = MaterialIconKind.CheckboxMultipleMarked,
            [typeof(NationalExamsPage)] = MaterialIconKind.ClipboardTextOutline,
            [typeof(LmdMarksPage)] = MaterialIconKind.PencilBoxMultipleOutline,
            [typeof(LmdResultsPage)] = MaterialIconKind.ScaleBalance,
            [typeof(LmdDebtsPage)] = MaterialIconKind.BookAlertOutline,
            [typeof(AttendancePage)] = MaterialIconKind.ClipboardCheck,
            [typeof(DisciplinePage)] = MaterialIconKind.AlertOctagon,
            [typeof(SchoolCyclesPage)] = MaterialIconKind.SchoolOutline,
            [typeof(LmdPlanningPage)] = MaterialIconKind.School,
            [typeof(RegulationPage)] = MaterialIconKind.BookOpenVariantOutline,
            [typeof(CurriculumAssignmentPage)] = MaterialIconKind.AccountTieVoiceOutline,
            [typeof(TeachingPage)] = MaterialIconKind.HumanMaleBoard,
            [typeof(TeacherAccountsPage)] = MaterialIconKind.AccountKey,
            [typeof(TeacherHomePage)] = MaterialIconKind.AccountSchool,
            [typeof(TeacherCurriculumPage)] = MaterialIconKind.BookEducationOutline,
            [typeof(DeliberationSettingsPage)] = MaterialIconKind.Tune,
            [typeof(AnonymousCodesPage)] = MaterialIconKind.Incognito,
            [typeof(MarksPage)] = MaterialIconKind.Pen,
            [typeof(EncodingProgressPage)] = MaterialIconKind.ProgressClock,
            [typeof(DeliberationPage)] = MaterialIconKind.Gavel,
            [typeof(StaffPage)] = MaterialIconKind.Briefcase,
            [typeof(StaffAttendancePage)] = MaterialIconKind.ClockCheck,
            [typeof(NetworkHubPage)] = MaterialIconKind.LanConnect,
            [typeof(SettingsPage)] = MaterialIconKind.Cog,
            [typeof(UsersPage)] = MaterialIconKind.AccountCog,
            [typeof(DataPage)] = MaterialIconKind.DatabaseExport,
            [typeof(CloudBackupPage)] = MaterialIconKind.CloudLock,
            [typeof(AuditPage)] = MaterialIconKind.ShieldCheck,
            [typeof(EraseDataPage)] = MaterialIconKind.DeleteSweep
        });

    public static MaterialIconKind ForPage(Type page, MaterialIconKind fallback) =>
        PageIcons.GetValueOrDefault(page, fallback);

    public static MaterialIconKind ForPage<TPage>() where TPage : PageViewModel => PageIcons[typeof(TPage)];

    /// <summary>Les couleurs des rubriques suivent le logo ; les alertes gardent leurs couleurs d'état.</summary>
    public static string NavigationTone(string original) => original is "ToneGreen" or "ToneTeal" or "TonePink"
        ? "ToneGreen" : "ToneBlue";
}
