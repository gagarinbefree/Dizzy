const { chromium } = require('playwright');
const assert = require('node:assert/strict');

async function main() {
  const browser = await chromium.launch({
    executablePath: process.env.CHROME_PATH || '/usr/bin/google-chrome',
    args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const ready = page.waitForEvent('console', {
      predicate: message => message.text().includes('DIZZY_TITLE_READY'), timeout: 120_000,
    });
    const url = process.env.DIZZY_WEB_URL || 'http://127.0.0.1:8765/';
    await Promise.all([ready, page.goto(url)]);
    assert.equal(await page.locator('canvas#canvas').isVisible(), true);
    await page.locator('canvas#canvas').focus();
    await Promise.all([
      page.waitForEvent('console', {
        predicate: message => /DIZZY_GAME_STARTED room=\d+/.test(message.text()), timeout: 30_000,
      }),
      page.keyboard.press('Enter'),
    ]);
    const manifest = await page.request.get(new URL('manifest.webmanifest', url).href);
    assert.equal(manifest.ok(), true);
    const data = await manifest.json();
    assert.equal(data.display, 'standalone');
    assert.equal(data.scope, './');
    for (const icon of data.icons) {
      assert.equal((await page.request.get(new URL(icon.src, url).href)).ok(), true, icon.src);
    }
    const version = await (await page.request.get(new URL('version.json', url).href)).json();
    assert.match(version.version, /^0\.1\.\d+$/);
    if (process.env.GITHUB_RUN_NUMBER) assert.equal(version.version, `0.1.${process.env.GITHUB_RUN_NUMBER}`);
    assert.deepEqual(errors, []);
    console.log(`Browser build ${version.version} starts and serves its PWA manifest.`);
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
