export interface Session {
  id: string; userName: string; displayName: string; role: string; roleLabel: string; permissions: string[];
  mustChangePassword: boolean; schoolName: string; schoolShortName?: string; currentYear?: string; currencySymbol: string; phoneCountryCode: string;
}
export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number; sum?: number | null }
export interface School {
  name: string; shortName: string; motto?: string; address?: string; city?: string; country?: string; phone?: string; email?: string; website?: string;
  currencyCode: string; currencySymbol: string; wholeAmountsOnly: boolean; receiptFooter?: string; hasLogo: boolean; phoneCountryCode: string;
}
export interface Period { id: string; order: number; name: string; month: number; calendarYear: number; dueDate: string }
export interface AcademicYear { id: string; label: string; startDate: string; endDate: string; status: string; isCurrent: boolean; periods: Period[] }
export interface Section { id: string; code: string; name: string; isActive: boolean }
export interface FeeItem { id: string; kind: string; label: string; amount: number; dueDate: string; order: number; periodId?: string }
export interface Promotion {
  id: string; academicYearId: string; code: string; name: string; sectionId?: string; sectionName?: string; level: number; capacity?: number;
  isActive: boolean; nextPromotionCode?: string; monthlyFee: number; annualTotal: number; studentCount: number; fees: FeeItem[];
}
export interface StudentListItem {
  id: string; enrollmentId?: string; matricule: string; lastName: string; middleName: string; firstName: string; gender: string;
  promotionCode?: string; status?: string; guardianPhone?: string; balance: number; overdue: number;
}
export interface Enrollment {
  id: string; academicYearId: string; academicYear: string; promotionId: string; promotionCode: string; promotionName: string;
  enrolledOn: string; status: string; exitDate?: string; exitReason?: string; isReEnrollment: boolean;
}
export interface Student {
  id: string; matricule: string; lastName: string; middleName: string; firstName: string; gender: string; birthDate?: string; birthPlace?: string;
  nationality?: string; address?: string; phone?: string; email?: string; guardianName?: string; guardianRelation?: string; guardianPhone?: string;
  guardianPhone2?: string; notes?: string; hasPhoto: boolean; enrollments: Enrollment[]; version: number;
}
export interface DuplicateCandidate { id: string; matricule: string; fullName: string; promotionCode?: string }
export interface AccountLine {
  feeItemId: string; label: string; dueDate: string; amount: number; covered: number; net: number; paid: number; remaining: number; status: string; isOverdue: boolean;
  kind: string;
}
export interface ReceiptSummary { id: string; number: string; date: string; amount: number; method: string; status: string; cashier: string; allocations?: string }
export interface Account {
  studentId: string; enrollmentId: string; matricule: string; fullName: string; promotionCode: string; academicYear: string; enrollmentStatus: string;
  lines: AccountLine[]; totalFees: number; totalCovered: number; totalNet: number; totalPaid: number; balance: number; overdue: number; overdueCount: number;
  agingBucket: string; receipts: ReceiptSummary[]; guardianPhone?: string; whatsAppLink?: string;
}
export interface AllocationPreview { feeItemId: string; label: string; amount: number; remainingAfter: number }
export interface PaymentPreview { allocations: AllocationPreview[]; balanceAfter: number }
export interface Receipt {
  id: string; number: string; verificationCode: string; date: string; studentId: string; matricule: string; studentName: string; promotionCode: string;
  academicYear: string; amount: number; method: string; reference?: string; payerName?: string; payerPhone?: string; note?: string; cashier: string;
  status: string; cancelledAtUtc?: string; cancelledBy?: string; cancellationReason?: string; allocations: AllocationPreview[]; createdAtUtc: string;
}
export interface ReceiptRow {
  id: string; number: string; date: string; matricule: string; studentName: string; promotionCode: string; amount: number; method: string; status: string;
  cashier: string; reference?: string;
}
export interface Sponsorship {
  id: string; enrollmentId: string; sponsorName: string; reason: string; effectiveFrom: string; isActive: boolean;
  lines: { feeItemId: string; label: string; percent: number; amount: number }[]; coveredTotal: number;
}
export interface SponsorshipListItem {
  id: string; enrollmentId: string; matricule: string; fullName: string; promotion: string; sponsorName: string; reason: string; effectiveFrom: string; isActive: boolean; covered: number;
}
export interface SeriesPoint { label: string; value: number; secondary?: number | null }
export interface PromotionStat { id: string; code: string; name: string; students: number; expected: number; collected: number; outstanding: number; overdue: number; recoveryRate: number }
export interface Dashboard {
  academicYear: string; activeStudents: number; newThisMonth: number; collectedTotal: number; collectedThisMonth: number; collectedToday: number;
  expectedToDate: number; outstanding: number; overdue: number; recoveryRate: number; studentsUpToDate: number; studentsLate: number;
  expensesThisMonth: number; monthlyCollections: SeriesPoint[]; promotions: PromotionStat[]; aging: SeriesPoint[]; latestReceipts: ReceiptSummary[];
  genderMale: number; genderFemale: number;
}
export interface Debtor {
  studentId: string; enrollmentId: string; matricule: string; fullName: string; promotionCode: string; balance: number; overdue: number; overdueCount: number;
  periods: string; agingBucket: string; guardianName?: string; guardianPhone?: string; whatsAppLink?: string; smsLink?: string;
}
export interface Expense {
  id: string; number: string; date: string; category: string; beneficiary: string; description: string; amount: number; method: string;
  reference?: string; recordedBy: string; status: string; cancellationReason?: string;
}
export interface CashMovement { date: string; type: string; number: string; label: string; method: string; in: number; out: number }
export interface CashSituation {
  periodStart: string; date: string; openingBalance: number; cashReceipts: number; cashExpenses: number; expectedCash: number; otherReceipts: number;
  lastClosingDate?: string; movements: CashMovement[];
}
export interface CashClosing {
  id: string; date: string; periodStart: string; openingBalance: number; cashReceipts: number; cashExpenses: number; expectedCash: number;
  countedCash: number; difference: number; note?: string; closedBy: string; closedAtUtc: string;
}
export interface User { id: string; userName: string; displayName: string; role: string; roleLabel: string; isActive: boolean; mustChangePassword: boolean; lastLoginUtc?: string; isLocked: boolean }
export interface AuditItem { id: number; atUtc: string; userName: string; action: string; entityType: string; entityId?: string; summary: string; ipAddress?: string }
export interface SearchHit { type: string; title: string; subtitle: string; url: string }
export interface Course { id: string; promotionId: string; code: string; name: string; coefficient: number; hours: number; teacherId?: string; teacherName?: string; isActive: boolean; assessmentCount: number }
export interface Assessment { id: string; courseId: string; title: string; kind: string; term: number; date: string; maxScore: number; weight: number; gradedCount: number }
export interface GradeRow { enrollmentId: string; matricule: string; fullName: string; score?: number | null; comment?: string | null }
export interface Results {
  promotion: string; term?: number; courses: string[]; coefficients: number[];
  rows: { enrollmentId: string; matricule: string; fullName: string; courseAverages: (number | null)[]; average?: number | null; rank?: number | null; decision: string }[];
  classAverage?: number | null; passed: number;
}
export interface AttendanceRow { enrollmentId: string; matricule: string; fullName: string; status?: string | null; note?: string | null }
export interface AttendanceSummary { enrollmentId: string; matricule: string; fullName: string; present: number; absent: number; late: number; justified: number; rate: number }
export interface Incident { id: string; enrollmentId: string; matricule: string; fullName: string; promotionCode: string; date: string; category: string; description: string; sanction?: string; severity: string; recordedBy: string }
export interface StaffMember { id: string; number: string; lastName: string; middleName: string; firstName: string; gender: string; function: string; phone?: string; email?: string; hireDate: string; baseSalary: number; isActive: boolean }
export interface Payroll { id: string; staffMemberId: string; number: string; fullName: string; function: string; period: string; baseSalary: number; allowances: number; deductions: number; advances: number; net: number; status: string; paidOn?: string; note?: string }
export interface BackupInfo { name: string; createdUtc: string; sizeBytes: number }
export interface ImportReport { applied: boolean; promotions: number; students: number; receipts: number; allocations: number; warnings: string[]; errors: string[] }
export interface ReEnrollmentItem { studentId: string; matricule: string; fullName: string; fromPromotion: string; toPromotion?: string; previousBalance: number; selected: boolean }
