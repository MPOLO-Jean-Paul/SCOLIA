// Prépare un serveur SCOLIA Pro de démonstration pour l'audit visuel par rôle :
// établissement de démonstration, un compte par rôle (secrétariat, comptabilité, promoteur,
// enseignant membre du jury), cours attribués, codes anonymes et quelques cotes encodées.
// Toutes les données sont fictives. Usage : node preparer-roles.mjs http://127.0.0.1:5293/
const base = process.argv[2] ?? "http://127.0.0.1:5293/";
const PASSWORD = "Scolia2026!";

function client() {
  let cookie = "";
  return async function call(method, path, body) {
    const response = await fetch(new URL(path, base), {
      method,
      headers: { "X-Scolia": "1", ...(cookie ? { Cookie: cookie } : {}), ...(body ? { "Content-Type": "application/json" } : {}) },
      body: body ? JSON.stringify(body) : undefined,
    });
    const set = response.headers.getSetCookie?.() ?? [];
    for (const c of set) if (c.startsWith("scolia.session=")) cookie = c.split(";")[0];
    const text = await response.text();
    if (!response.ok) throw new Error(`${method} ${path} : ${response.status} ${text.slice(0, 300)}`);
    return text ? JSON.parse(text) : null;
  };
}

async function waitServer() {
  for (let i = 0; i < 120; i++) {
    try { const r = await fetch(new URL("api/sante", base)); if (r.status < 500) return; } catch { }
    await new Promise(r => setTimeout(r, 1000));
  }
  throw new Error("Serveur SCOLIA injoignable.");
}

async function firstLogin(userName, provisional) {
  const c = client();
  await c("POST", "api/session", { userName, password: provisional });
  await c("POST", "api/session/mot-de-passe", { currentPassword: provisional, newPassword: PASSWORD });
  return c;
}

await waitServer();
const admin = client();
const today = new Date();
const first = today.getMonth() >= 7 ? today.getFullYear() : today.getFullYear() - 1;
await admin("POST", "api/installation", {
  schoolName: "Institut Technique Médical de démonstration", shortName: "ITM", motto: "Servir et soigner", city: "Lubumbashi",
  phone: "0810000000", currencyCode: "CDF", currencySymbol: "FC", yearLabel: `${first}-${first + 1}`, startMonth: 9, monthCount: 10,
  adminUserName: "admin", adminDisplayName: "Marie Secrétaire", adminPassword: PASSWORD, loadDemoData: true,
});
await admin("POST", "api/session", { userName: "admin", password: PASSWORD });

await admin("POST", "api/utilisateurs", { userName: "comptable", displayName: "Grace Comptable", role: "Comptabilite", password: "Provisoire1" });
await admin("POST", "api/utilisateurs", { userName: "promoteur", displayName: "Jean Promoteur", role: "Promoteur", password: "Provisoire1" });
const teacher = await admin("POST", "api/comptes-enseignants/nouvel-enseignant", {
  lastName: "LUMBALA", middleName: "", firstName: "Paul", gender: "M", phone: "0812345678", createAccount: true,
  userName: "enseignant", juryAccess: true,
});

const promotions = await admin("GET", "api/etablissement/promotions");
const assigned = [];
for (const promotion of promotions.slice(0, 2)) {
  const courses = await admin("GET", `api/scolarite/cours?promotionId=${promotion.id}`);
  for (const course of courses.slice(0, 2)) {
    await admin("PUT", `api/scolarite/cours/${course.id}/attribution`, { teacherId: teacher.staffMemberId, confirmReplace: true });
    assigned.push(course.id);
  }
  await admin("POST", "api/deliberation/codes/attribution", { promotionId: promotion.id });
}

await firstLogin("comptable", "Provisoire1");
await firstLogin("promoteur", "Provisoire1");
const prof = await firstLogin("enseignant", teacher.account.provisionalCode);

// L'enseignant encode la moitié de la première feuille : l'espace enseignant montre un avancement réel.
const sheet = await prof("GET", `api/deliberation/feuille?coursId=${assigned[0]}&nature=S1`);
const rows = sheet.rows.slice(0, Math.ceil(sheet.rows.length / 2)).map((r, i) => ({ enrollmentId: r.enrollmentId, value: 8 + (i % 12) }));
await prof("PUT", `api/deliberation/feuille?coursId=${assigned[0]}&nature=S1`, rows);

console.log(JSON.stringify({ comptes: ["admin", "comptable", "promoteur", "enseignant"], coursAttribues: assigned.length, cotes: rows.length }));
