const { chromium } = require('playwright');
const path = require('path');
(async () => {
  const [f, out, ...times] = process.argv.slice(2);
  const W = f === 'v' ? 1080 : 1920, H = f === 'v' ? 1920 : 1080;
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: W, height: H } });
  page.on('console', m => console.log('console:', m.text()));
  page.on('pageerror', e => console.log('error:', e.message));
  await page.goto('file://' + path.join(__dirname, 'index.html') + '?f=' + f);
  await page.evaluate(() => window.ready());
  for (const t of times) {
    await page.evaluate(t => window.render(t), Number(t));
    await page.screenshot({ path: `${out}/${f}-${t}.png` });
  }
  await browser.close();
})();
