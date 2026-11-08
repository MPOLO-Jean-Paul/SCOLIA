import test from "node:test";
import assert from "node:assert/strict";
import { evaluateLicense, validateLicensePatch, DAY_MS } from "../src/licenses.mjs";

const now = Date.parse("2026-11-08T12:00:00Z");
const school = {
  id:"school-test", name:"Ecole", created_at: now - 12 * DAY_MS,
  billing_enabled:0, trial_days:30, paid_until:null, grace_until:null,
  manual_override:"none", override_until:null
};

test("Aucune école n'est verrouillée avant l'activation de la facturation",()=>{
  const license=evaluateLicense(school,now);
  assert.equal(license.readonly,false);
  assert.equal(license.access_mode,"full");
  assert.equal(license.license_status,"free");
  assert.equal(license.days_remaining,null);
});
test("Facturation automatique : période d'essai calculée depuis la création du serveur",()=>{
  const license=evaluateLicense({...school,billing_enabled:1},now);
  assert.equal(license.license_status,"trial");
  assert.equal(license.days_remaining,18);
  assert.equal(license.access_mode,"full");
});
test("Échéance atteinte : écriture interdite, lecture conservée",()=>{
  const x=evaluateLicense({...school,billing_enabled:1,trial_days:10},now);
  assert.equal(x.access_mode,"read_only");
  assert.equal(x.reason,"echeance_atteinte");
  assert.equal(x.offline_grace_seconds,7*86400);
});
test("Paiement validé et prolongation des jours disponibles",()=>{
  const x=evaluateLicense({...school,billing_enabled:1,trial_days:10,paid_until:now+40*DAY_MS},now);
  assert.equal(x.access_mode,"full");
  assert.equal(x.days_remaining,40);
  assert.equal(x.license_status,"active");
});
test("Clémence manuelle immédiate, même après expiration automatique",()=>{
  const x=evaluateLicense({...school,billing_enabled:1,trial_days:10,
    manual_override:"allow",override_until:now+7*DAY_MS},now);
  assert.equal(x.readonly,false);
  assert.equal(x.days_remaining,7);
  assert.equal(x.license_status,"derogation");
});
test("Blocage manuel immédiat, même si l'abonnement a été payé",()=>{
  const x=evaluateLicense({...school,billing_enabled:1,paid_until:now+40*DAY_MS,
    manual_override:"read_only"},now);
  assert.equal(x.readonly,true);
  assert.equal(x.reason,"blocage_manuel");
});
test("Levée d'une restriction manuelle : retour au calcul automatique",()=>{
  const x=evaluateLicense({...school,billing_enabled:1,paid_until:now+5*DAY_MS,
    manual_override:"none"},now);
  assert.equal(x.readonly,false);
});
test("Délai de grâce automatique après la période d'essai",()=>{
  const x=evaluateLicense({...school,billing_enabled:1,trial_days:10,
    grace_until:now+2*DAY_MS},now);
  assert.equal(x.license_status,"grace");
  assert.equal(x.days_remaining,2);
});
test("Rejette les dates, modes et périodes d'essai invalides",()=>{
  assert.equal(validateLicensePatch({manual_override:"hidden_superuser"}),null);
  assert.equal(validateLicensePatch({trial_days:10000}),null);
  assert.equal(validateLicensePatch({billing_enabled:"true"}),null);
  assert.equal(validateLicensePatch({paid_until:"2026-11-08"}),null);
  assert.deepEqual(validateLicensePatch({billing_enabled:true,trial_days:30}),
    {billing_enabled:1,trial_days:30});
});
