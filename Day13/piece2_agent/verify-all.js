const { chromium } = require('playwright');
const fs = require('fs');

const SVC = 'c:\\Users\\LENOVO\\OneDrive\\Desktop\\Thinkschool\\Day13\\piece1\\quotes-ui\\src\\app\\quotes\\quotes-feature.service.ts';

function readSvc() { return fs.readFileSync(SVC, 'utf8'); }
function writeSvc(s) { fs.writeFileSync(SVC, s, 'utf8'); }
function wait(ms) { return new Promise(r => setTimeout(r, ms)); }

(async () => {
  const browser = await chromium.launch({ headless: true });

  // ── STEP 1+2: list loads + detail loads ──────────────────────────
  {
    console.log('\n=== STEP 1+2: LIST + DETAIL ===');
    const page = await browser.newPage();
    await page.goto('http://localhost:4200', { waitUntil: 'networkidle', timeout: 15000 });
    await wait(1500);

    const btnCount = await page.locator('app-quotes-list aside button').count();
    const firstAuthor = btnCount > 0
      ? (await page.locator('app-quotes-list aside button').first().locator('span').first().textContent()).trim()
      : '(none)';
    console.log(`List: ${btnCount} buttons. First author: "${firstAuthor}"`);
    await page.screenshot({ path: 'step1-list.png' });

    if (btnCount > 0) {
      await page.locator('app-quotes-list aside button').first().click();
      await wait(1200);
      const bq = await page.locator('app-quotes-list main blockquote').textContent().catch(() => 'NOT FOUND');
      const auth = await page.locator('app-quotes-list main p').first().textContent().catch(() => 'NOT FOUND');
      const dt = await page.locator('app-quotes-list main p').nth(1).textContent().catch(() => 'NOT FOUND');
      console.log(`Detail blockquote[60]: "${bq.trim().slice(0, 60)}"`);
      console.log(`Author: "${auth.trim()}" | Date: "${dt.trim()}"`);
      await page.screenshot({ path: 'step2-detail.png' });
    }
    await page.close();
  }

  // ── STEP 3: Loading state — delay(2000) on listQuotes ─────────────
  {
    console.log('\n=== STEP 3: LOADING STATE ===');
    const orig = readSvc();
    // Normalise line endings so replace works on Windows
    const norm = orig.replace(/\r\n/g, '\n');
    const patched = norm
      .replace(
        "import { catchError } from 'rxjs/operators';",
        "import { catchError, delay } from 'rxjs/operators';"
      )
      .replace(
        // First .pipe(catchError...) is inside listQuotes
        '.pipe(catchError(this.handleError));',
        '.pipe(delay(2000), catchError(this.handleError));'
      );
    const matched = patched.includes('delay(2000)');
    writeSvc(patched);
    console.log(`Patched (delay(2000) inserted: ${matched}). Waiting for rebuild...`);
    await wait(4000);

    const page = await browser.newPage();
    await page.goto('http://localhost:4200', { waitUntil: 'domcontentloaded', timeout: 10000 });
    await wait(400);
    const loadingText = await page.locator('app-quotes-list aside p').first().textContent().catch(() => '');
    console.log(`Text at t=0.4s: "${loadingText.trim()}"`);
    await page.screenshot({ path: 'step3-loading-during.png' });
    await wait(2800);
    const btnCount = await page.locator('app-quotes-list aside button').count();
    console.log(`After 3.2s: ${btnCount} buttons visible`);
    await page.screenshot({ path: 'step3-loading-after.png' });
    await page.close();

    writeSvc(orig);
    console.log('Restored.');
    await wait(4000);
  }

  // ── STEP 4: Error state ────────────────────────────────────────────
  {
    console.log('\n=== STEP 4: ERROR STATE ===');
    const orig = readSvc();
    const norm = orig.replace(/\r\n/g, '\n');
    const patched = norm.replace(
      '`/api/quotes?page=${page}&size=${size}`',
      '`/api/quotes-broken?page=${page}&size=${size}`'
    );
    const matched = patched.includes('quotes-broken');
    writeSvc(patched);
    console.log(`Patched URL to /api/quotes-broken (matched: ${matched}). Waiting for rebuild...`);
    await wait(4000);

    const page = await browser.newPage();
    const pageErrors = [];
    page.on('pageerror', e => pageErrors.push(e.message));
    await page.goto('http://localhost:4200', { waitUntil: 'networkidle', timeout: 10000 });
    await wait(1000);
    const errText = await page.locator('app-quotes-list aside p').first().textContent().catch(() => '');
    console.log(`Error text: "${errText.trim()}"`);
    console.log(`Uncaught JS errors: ${pageErrors.length}`);
    await page.screenshot({ path: 'step4-error.png' });
    await page.close();

    writeSvc(orig);
    console.log('Restored.');
    await wait(4000);
  }

  // ── STEP 5: Empty state ────────────────────────────────────────────
  {
    console.log('\n=== STEP 5: EMPTY STATE ===');
    const orig = readSvc();
    const norm = orig.replace(/\r\n/g, '\n');
    const patched = norm.replace(
      '`/api/quotes?page=${page}&size=${size}`',
      '`/api/quotes?page=${page}&size=0`'
    );
    const matched = patched.includes('size=0');
    writeSvc(patched);
    console.log(`Patched size=0 (matched: ${matched}). Waiting for rebuild...`);
    await wait(4000);

    const page = await browser.newPage();
    await page.goto('http://localhost:4200', { waitUntil: 'networkidle', timeout: 10000 });
    await wait(1000);
    const emptyText = await page.locator('app-quotes-list aside p').first().textContent().catch(() => '');
    console.log(`Empty text: "${emptyText.trim()}"`);
    await page.screenshot({ path: 'step5-empty.png' });
    await page.close();

    writeSvc(orig);
    console.log('Restored.');
    await wait(4000);
  }

  // ── STEP 6: Race guard — delay(3000) on getQuote ──────────────────
  {
    console.log('\n=== STEP 6: RACE GUARD ===');
    const orig = readSvc();
    const norm = orig.replace(/\r\n/g, '\n');
    // getQuote pipe is the SECOND .pipe(catchError...) — use replace with count
    let count = 0;
    const patched = norm
      .replace(
        "import { catchError } from 'rxjs/operators';",
        "import { catchError, delay } from 'rxjs/operators';"
      )
      .replace(/\.pipe\(catchError\(this\.handleError\)\);/g, (match) => {
        count++;
        // Replace only the second occurrence (getQuote)
        return count === 2
          ? '.pipe(delay(3000), catchError(this.handleError));'
          : match;
      });
    writeSvc(patched);
    console.log(`Patched: delay(3000) in getQuote (count=${count} matches, replaced #2). Waiting for rebuild...`);
    await wait(4000);

    const page = await browser.newPage();
    await page.goto('http://localhost:4200', { waitUntil: 'networkidle', timeout: 15000 });
    await wait(1000);

    const buttons = page.locator('app-quotes-list aside button');
    const cnt = await buttons.count();
    if (cnt >= 2) {
      const authorA = (await buttons.nth(0).locator('span').first().textContent()).trim();
      const authorB = (await buttons.nth(1).locator('span').first().textContent()).trim();
      console.log(`Click A (idx=0): "${authorA}"  then 300ms later click B (idx=1): "${authorB}"`);

      await buttons.nth(0).click();
      await wait(300);
      await buttons.nth(1).click();
      console.log('Both clicked. Waiting 3.5s for switchMap response...');
      await wait(3500);

      const detailAuth = await page.locator('app-quotes-list main p').first().textContent().catch(() => '(none)');
      const detailBq = await page.locator('app-quotes-list main blockquote').textContent().catch(() => '');
      console.log(`Detail author: "${detailAuth.trim()}"`);
      console.log(`Detail text[40]: "${detailBq.trim().slice(0, 40)}"`);

      // Check which button is highlighted (#eef2ff = selected)
      const highlightedIdx = await page.evaluate(() => {
        const btns = Array.from(document.querySelectorAll('app-quotes-list aside button'));
        return btns.findIndex(b => getComputedStyle(b).backgroundColor === 'rgb(238, 242, 255)');
      });
      console.log(`Highlighted button index: ${highlightedIdx} (expected 1 = B)`);
      console.log(`SwitchMap cancelled A: ${highlightedIdx === 1 ? 'YES ✓' : 'NO ✗'}`);
    } else {
      console.log(`Only ${cnt} buttons — skipping race test`);
    }
    await page.screenshot({ path: 'step6-race.png' });
    await page.close();

    writeSvc(orig);
    console.log('Restored.');
    await wait(3000);
  }

  await browser.close();
  console.log('\n=== All steps complete ===');
})().catch(e => { console.error('FATAL:', e.message); process.exit(1); });
