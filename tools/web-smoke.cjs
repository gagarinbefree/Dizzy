const { chromium } = require('playwright');
const assert = require('node:assert/strict');

async function checkGameMemory(page) {
  // The renderer leaked native image storage while standing still in room 74.
  // Chrome's JavaScript heap counters omit Godot's WebAssembly linear memory.
  const session = await page.context().newCDPSession(page);
  try {
    const { result: prototype } = await session.send('Runtime.evaluate', { expression: 'WebAssembly.Memory.prototype' });
    const { objects } = await session.send('Runtime.queryObjects', { prototypeObjectId: prototype.objectId });
    async function readMiB() {
      const { result } = await session.send('Runtime.callFunctionOn', {
        objectId: objects.objectId,
        functionDeclaration: 'function () { return Math.max(...this.map(memory => memory.buffer.byteLength)) / 1048576; }',
        returnByValue: true,
      });
      assert.ok(result.value > 0, 'Expected live WebAssembly memory');
      return result.value;
    }
    const before = await readMiB();
    await page.waitForTimeout(45_000);
    const after = await readMiB();
    console.log(`WebAssembly memory over 45 seconds of gameplay: ${before} -> ${after} MiB`);
    assert.ok(after - before < 96, 'WebAssembly memory grew continuously during idle gameplay');
  } finally {
    await session.detach();
  }
}

async function main() {
  const browser = await chromium.launch({
    executablePath: process.env.CHROME_PATH || '/usr/bin/google-chrome',
    args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => {
      console.error(error.message);
      errors.push(error.message);
    });
    await page.addInitScript(() => {
      window.downloadPercentages = [];
      document.addEventListener('DOMContentLoaded', () => {
        const progress = document.querySelector('#status-progress');
        new MutationObserver(() => window.downloadPercentages.push(progress.value))
          .observe(progress, { attributes: true, attributeFilter: ['value'] });
      });
      const play = HTMLMediaElement.prototype.play;
      HTMLMediaElement.prototype.play = function (...args) {
        if (this.src.endsWith('/0-title.mp3')) window.loadingTheme = this;
        return play.apply(this, args);
      };
      window.footstepLengths = new Set();
      const createBuffer = BaseAudioContext.prototype.createBuffer;
      BaseAudioContext.prototype.createBuffer = function (...args) {
        const buffer = createBuffer.apply(this, args);
        if (buffer.duration < 0.05) window.footstepLengths.add(buffer.length);
        return buffer;
      };
    });
    const url = process.env.DIZZY_WEB_URL || 'http://127.0.0.1:8765/';
    let continueBoot;
    const bootGate = new Promise(resolve => { continueBoot = resolve; });
    await page.route('**/version.json?*', async route => { await bootGate; await route.continue(); });
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => !document.querySelector('#enable-music').hidden || window.loadingTheme?.currentTime > 0);
    const musicButton = page.locator('#enable-music');
    if (await musicButton.isVisible()) await musicButton.click();
    await page.waitForFunction(() => window.loadingTheme.currentTime > 0.25);
    await page.evaluate(() => {
      const finish = window.dizzyLoadingMusic.finish;
      window.dizzyLoadingMusic.finish = () => (window.titleMusicOffset = finish());
    });
    const ready = page.waitForEvent('console', {
      predicate: message => message.text().includes('DIZZY_TITLE_READY'), timeout: 120_000,
    });
    assert.equal(await page.locator('#status-progress').evaluate(bar => bar.position), -1);
    continueBoot();
    await ready;
    async function checkDownloadProgress() {
      await page.waitForFunction(() => window.downloadPercentages.includes(100));
      const values = await page.evaluate(() => window.downloadPercentages);
      assert.equal(values[0], 0);
      assert.deepEqual(values, [...values].sort((a, b) => a - b));
      console.log('Download progress (%):', values.join(', '));
    }
    await checkDownloadProgress();
    assert.ok(await page.evaluate(() => window.titleMusicOffset > 0 && window.loadingTheme.paused));
    assert.equal(await page.locator('canvas#canvas').isVisible(), true);
    await page.locator('canvas#canvas').focus();
    await Promise.all([
      page.waitForEvent('console', {
        predicate: message => /DIZZY_GAME_STARTED room=\d+/.test(message.text()), timeout: 30_000,
      }),
      page.keyboard.press('Enter'),
    ]);
    await checkGameMemory(page);
    await page.evaluate(() => window.footstepLengths.clear());
    await page.keyboard.down('ArrowRight');
    try {
      // The two alternating footsteps have different lengths. This also works
      // when the browser plays the samples through an AudioWorklet.
      await page.waitForFunction(() => window.footstepLengths.size >= 2, null, { timeout: 30_000 });
    } finally {
      await page.keyboard.up('ArrowRight');
    }
    await page.keyboard.down('Space');
    await page.waitForTimeout(300);
    await page.keyboard.up('Space');
    await page.keyboard.down('q');
    await page.waitForTimeout(500);
    await page.keyboard.up('q');
    await Promise.all([
      page.waitForEvent('console', {
        predicate: message => message.text().includes('DIZZY_GAME_STARTED room=74'), timeout: 10_000,
      }),
      page.keyboard.press('Enter'),
    ]);
    const manifestUrl = await page.locator('link[rel="manifest"]').evaluate(link => link.href);
    const manifest = await page.request.get(manifestUrl);
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
    if (version.build) {
      const buildUrl = new URL(`builds/${version.build}/`, url);
      assert.equal(page.url(), url);
      const runtime = await page.evaluate(() => performance.getEntriesByType('resource')
        .map(entry => entry.name).filter(name => name.includes('/_framework/')));
      assert.ok(runtime.length > 0 && runtime.every(name => name.startsWith(buildUrl.href)));
      const wasmUrl = new URL('godot.wasm', buildUrl).href;
      await page.waitForFunction(async url => Boolean(await caches.match(url)), wasmUrl);
      await page.context().setOffline(true);
      try {
        const magic = await page.evaluate(async url => {
          const response = await fetch(url, { cache: 'no-store' });
          const reader = response.body.getReader();
          const chunk = await reader.read();
          void reader.cancel();
          return Array.from(chunk.value.slice(0, 4));
        }, wasmUrl);
        assert.deepEqual(magic, [0, 97, 115, 109], 'Expected the cached WebAssembly engine');
      } finally {
        await page.context().setOffline(false);
      }
      await page.reload({ waitUntil: 'domcontentloaded' });
      await checkDownloadProgress();
    }
    assert.deepEqual(errors, []);
    console.log(`Browser build ${version.version} played footsteps and started a second game after movement and jump input.`);
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
