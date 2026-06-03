import { chromium } from 'playwright';

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();

// STEP 1: List loads
await page.goto('http://localhost:4200', { waitUntil: 'networkidle' });
const listTitle = await page.locator('app-quotes-list h2').textContent().catch(() => 'NOT FOUND');
const quoteButtons = await page.locator('app-quotes-list aside button').count();
const firstAuthor = quoteButtons > 0
  ? await page.locator('app-quotes-list aside button').first().locator('span').first().textContent()
  : 'none';

console.log('=== STEP 1: List loads ===');
console.log('List heading:', listTitle);
console.log('Quote buttons count:', quoteButtons);
console.log('First author:', firstAuthor?.trim());

// Screenshot
await page.screenshot({ path: 'step1-list-loads.png' });

// STEP 2: Detail loads - click first quote
await page.locator('app-quotes-list aside button').first().click();
await page.waitForTimeout(1000);

const detailText = await page.locator('app-quotes-list main blockquote').textContent().catch(() => 'NOT FOUND');
const detailAuthor = await page.locator('app-quotes-list main p').first().textContent().catch(() => 'NOT FOUND');
const detailDate = await page.locator('app-quotes-list main p').nth(1).textContent().catch(() => 'NOT FOUND');

console.log('\n=== STEP 2: Detail loads ===');
console.log('Detail blockquote (first 80 chars):', detailText?.trim().slice(0, 80));
console.log('Detail author:', detailAuthor?.trim());
console.log('Detail date:', detailDate?.trim());

await page.screenshot({ path: 'step2-detail-loads.png' });

await browser.close();
console.log('\nDone');
