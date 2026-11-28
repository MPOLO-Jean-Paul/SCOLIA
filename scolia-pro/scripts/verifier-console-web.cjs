// Vrai navigateur, vrai Worker, écoles fictives et SQLite en mémoire uniquement.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const assert = require('assert/strict');
const { spawn } = require('child_process');
const { once } = require('events');
const { createInterface } = require('readline');
(async()=>{
 const fixture=spawn(process.execPath,[path.join(__dirname,'../deploy/owner-console/tests/fixtures/native-service.mjs')],{stdio:['ignore','pipe','inherit']});
 const lines=createInterface({input:fixture.stdout});
 const [base]=await once(lines,'line');lines.close();
 let browser;
 try {
 browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH||undefined,headless:true});
 const destination=process.env.SCOLIA_CONSOLE_WEB_CAPTURES||path.join(__dirname,'../artifacts/console-premium/web');
 fs.mkdirSync(destination,{recursive:true});
 const report=[];
 for(const [name,width,height,dark] of [['desktop',1440,1000,false],['mobile',390,844,false],['petit-mobile',320,640,false],['desktop-sombre',1280,820,true],['mobile-sombre',390,844,true]]){
  const context=await browser.newContext({viewport:{width,height},colorScheme:dark?'dark':'light',extraHTTPHeaders:{'x-scolia-test-access':'1'}});
  const page=await context.newPage();
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.goto(base+'console');
  await page.getByLabel('Jeton propriétaire').fill('owner-console-integration-fictif-2026');
  await page.getByLabel('Jeton propriétaire').press('Enter');
  await page.locator('#workspace').waitFor({state:'visible'});
  await page.waitForFunction(()=>document.getElementById('schools').textContent==='2');
  assert.equal(await page.locator('#rows tr').count(),2);
  assert.equal(await page.locator('#serverRows tr').count(),2);
  const anonymous=await context.request.get(base+'api/v1/owner/summary',{headers:{'x-scolia-test-access':'1'}});
  assert.equal(anonymous.status(),401);
  await page.screenshot({path:path.join(destination,name+'-accueil.png'),fullPage:false});
  const row=page.locator('#rows tr').filter({hasText:'École Démonstration JP-Services'});
  await row.getByRole('button',{name:'Consulter'}).click();
  await page.locator('#detailsPanel').waitFor({state:'visible'});
  await page.locator('#detailsPanel').screenshot({path:path.join(destination,name+'-etablissement.png')});
  const overflow=await page.evaluate(()=>({width:innerWidth,document:document.documentElement.scrollWidth,body:document.body.scrollWidth}));
  assert.ok(overflow.document<=width+1 && overflow.body<=width+1,JSON.stringify({name,overflow}));
  assert.deepEqual(errors,[],name);
  await page.locator('#logout').click();
  await page.locator('#auth').waitFor({state:'visible'});
  for(const id of ['rows','serverRows','auditRows','relayRows','cloudCards'])assert.equal(await page.locator('#'+id).evaluate(el=>el.childElementCount),0,id);
  assert.equal(await page.locator('#schools').textContent(),'');
  report.push({name,width,height,errors,overflow});
  await context.close();
 }

 // Contrat métier réel : un seul paiement confirmé, puis une réponse tardive après verrouillage.
 const context=await browser.newContext({viewport:{width:390,height:844},extraHTTPHeaders:{'x-scolia-test-access':'1'}});
 const page=await context.newPage();
 page.on('dialog',dialog=>dialog.accept());
 await page.goto(base+'console');
 await page.getByLabel('Jeton propriétaire').fill('owner-console-integration-fictif-2026');
 await page.locator('#connect').click();
 await page.waitForFunction(()=>document.getElementById('schools').textContent==='2');
 const schools=await (await context.request.get(base+'api/v1/owner/schools',{headers:{Authorization:'Bearer owner-console-integration-fictif-2026'}})).json();
 const school=schools.schools.find(s=>s.name==='École Démonstration JP-Services');
 const row=page.locator('#rows tr').filter({hasText:school.name});
 await row.getByRole('button',{name:'Consulter'}).click();
 await page.locator('#detailsPanel').waitFor({state:'visible'});
 assert.ok((await page.locator('#licenseSummary').textContent()).includes('LECTURE SEULE'));
 await page.locator('#paidTo').fill('2027-12-31');
 let patches=0;page.on('request',r=>{if(r.method()==='PATCH'&&r.url().endsWith('/license'))patches++;});
 await page.locator('#savePayment').click();
 await page.waitForFunction(()=>document.getElementById('licenseSummary').textContent.includes('accès complet'));
 assert.equal(patches,1);
 const paid=await (await context.request.get(base+'api/v1/owner/schools/'+school.id,{headers:{Authorization:'Bearer owner-console-integration-fictif-2026'}})).json();
 assert.equal(paid.license.readonly,false);assert.equal(paid.school.manual_override,'none');
 let release,started;const gate=new Promise(resolve=>release=resolve), pending=new Promise(resolve=>started=resolve);
 await page.route('**/api/v1/owner/schools/'+school.id,async route=>{const response=await route.fetch();started();await gate;try{await route.fulfill({response});}catch{}});
 await page.locator('#rows tr').filter({hasText:school.name}).getByRole('button',{name:'Consulter'}).click();
 await pending;await page.locator('#logout').click();release();
 await page.locator('#auth').waitFor({state:'visible'});
 await page.waitForFunction(()=>document.getElementById('workspace').hidden&&document.getElementById('detailsPanel').hidden);
 assert.equal(await page.locator('#details').textContent(),'');
 assert.equal(await page.locator('#token').inputValue(),'');
 report.push({scenario:'paiement unique et verrouillage pendant une lecture',paid:!paid.license.readonly,patches});
 await context.close();
 fs.writeFileSync(path.join(destination,'verification.json'),JSON.stringify(report,null,2));
 console.log(JSON.stringify(report));
 } finally {await browser?.close();fixture.kill('SIGTERM');}
})().catch(error=>{console.error(error);process.exitCode=1;});
