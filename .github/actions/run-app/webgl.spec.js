const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const timeout = parseInt(process.env.APP_TIMEOUT || '1800') * 1000;
const mode = process.env.APP_MODE || 'test';
const suite = process.env.APP_SUITE || 'all';
const webgpu = process.env.WEB_GRAPHICS_API === 'webgpu';

function saveScreenshots(screenshots) {
  if (!screenshots || screenshots.length === 0) return;
  fs.mkdirSync('screenshots', { recursive: true });
  for (const s of screenshots) {
    fs.writeFileSync(path.join('screenshots', `${s.name}.png`), Buffer.from(s.data, 'base64'));
  }
  console.log(`Saved ${screenshots.length} screenshot(s)`);
}

async function requireWebGpuAdapter(page) {
  const adapter = await page.evaluate(async () => {
    if (!navigator.gpu) return null;
    const found = await navigator.gpu.requestAdapter();
    if (!found) return null;
    const info = found.info || {};
    return [info.vendor, info.architecture, info.description].filter(Boolean).join(' ') || 'unnamed';
  });
  expect(adapter, 'the CI browser exposes no WebGPU adapter').toBeTruthy();
  console.log(`WebGPU adapter: ${adapter}`);
}

async function collectScreenshots(page) {
  await page.waitForFunction(() => !window.unityTestScreenshotsPending, { timeout });
  saveScreenshots(await page.evaluate(() => window.unityTestScreenshots || []));
}

async function run(page) {
  const url = mode === 'benchmark'
    ? `https://localhost:8080/?suite=${encodeURIComponent(suite)}`
    : 'https://localhost:8080';
  await page.goto(url, { waitUntil: 'networkidle', timeout: 60000 });
  if (webgpu) await requireWebGpuAdapter(page);

  if (mode === 'benchmark') {
    await page.waitForFunction(() => window.unityBenchmarkComplete === true, { timeout });
    const json = await page.evaluate(() => window.unityBenchmarkResults);
    expect(json).toBeTruthy();
    fs.writeFileSync('benchmarkResults.json', json);
    console.log('Benchmark results saved');

    await collectScreenshots(page);
  } else {
    await page.waitForFunction(() => window.unityTestsComplete === true, { timeout });
    const results = await page.evaluate(() => window.unityTestResults);
    fs.writeFileSync('testResults.xml', results.xml);
    console.log(`Tests: ${results.passed}/${results.total} passed`);

    await collectScreenshots(page);

    expect(results.allPassed).toBe(true);
  }
}

test(`UniText ${webgpu ? 'WebGPU' : 'WebGL'} ${mode}`, async ({ page }) => {
  let fail;
  const browserFailure = new Promise((_, reject) => fail = reject);
  page.on('console', msg => console.log('[Browser]', msg.text()));
  page.on('pageerror', fail);

  await Promise.race([run(page), browserFailure]);
});
