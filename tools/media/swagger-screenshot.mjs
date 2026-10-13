import { chromium } from 'playwright';

const base = process.env.LUMEN_URL ?? 'http://localhost:5219';
const out = process.argv[2] ?? 'docs/media/swagger.png';

const res = await fetch(`${base}/api/auth/token`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'admin@acme.test', password: 'Passw0rd!' }),
});
if (!res.ok) throw new Error(`login failed: ${res.status}`);
const { token } = await res.json();

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 1600 }, deviceScaleFactor: 2 });
await page.goto(`${base}/swagger/index.html`);
await page.locator('.opblock').first().waitFor();

await page.locator('button.authorize').first().click();
await page.locator('.modal-ux input[type=text]').fill(token);
await page.locator('.modal-ux button.authorize').click();
await page.locator('.modal-ux button.btn-done').click();

const ask = page.locator('#operations-Ask-post_api_Ask');
await ask.locator('.opblock-summary').click();
await ask.locator('button.try-out__btn').click();
await ask.locator('textarea.body-param__text').fill(JSON.stringify(
  { question: 'How many shareholders of record did Apple have as of October 17, 2025?' }, null, 2));
await ask.locator('button.execute').click();
await ask.locator('.live-responses-table .response-col_status').first().waitFor({ timeout: 60000 });

await page.addStyleTag({ content: `
  .curl-command, .request-url, .response-col_description__inner + div h5, .responses-table:not(.live-responses-table) { display: none !important; }
  textarea.body-param__text { height: 70px !important; min-height: 70px !important; }
  .live-responses-table .highlight-code .microlight { max-height: 360px; overflow: hidden !important; }
` });
await page.addStyleTag({ content: '.live-responses-table .response-col_description > div:not(:first-of-type) { display: none !important; }' });

const last = await page.locator('.opblock').last().boundingBox();
await page.screenshot({ path: out, fullPage: true, clip: { x: 0, y: 0, width: 1280, height: Math.ceil(last.y + last.height + 24) } });
await browser.close();
console.log(`saved ${out}`);
