const assert=require('node:assert/strict');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/Guroc/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
  const browser=await chromium.connectOverCDP('http://127.0.0.1:9422');
  try {
    const pages=browser.contexts().flatMap(c=>c.pages());
    const page=pages.find(p=>p.url().startsWith('https://0.0.0.1/'));
    assert(page,'Native app WebView not found');
    await page.locator('#setup').waitFor({state:'visible',timeout:20000});
    assert.equal(await page.locator('#error').isVisible(),false,await page.locator('#error').textContent());
    assert.match(await page.locator('#status').innerText(),/Add your games folder/);
    assert(await page.locator('#host-pairing').isVisible());
    assert.equal(await page.locator('.game').count(),0);
    await page.screenshot({path:require('node:path').resolve(__dirname,'../dist/native-windows.png'),fullPage:true});
    console.log('PASS: published Windows executable starts, loads its embedded UI, and completes a real JavaScript-to-.NET setup call.');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
