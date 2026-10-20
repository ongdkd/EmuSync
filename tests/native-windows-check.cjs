const assert=require('node:assert/strict');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/Guroc/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
  const browser=await chromium.connectOverCDP(process.env.EMUSYNC_CDP_URL||'http://127.0.0.1:9422');
  try {
    let page;
    for(let attempt=0;attempt<100;attempt++) {
      page=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://0.0.0.1/'));
      if(page) break;
      await new Promise(resolve=>setTimeout(resolve,200));
    }
    assert(page,'Native app WebView not found');
    await page.locator('#setup').waitFor({state:'visible',timeout:20000});
    assert.equal(await page.locator('#error').isVisible(),false,await page.locator('#error').textContent());
    assert.match(await page.locator('#library-status').textContent(),/Add your games folder/);
    assert(await page.locator('#host-pairing').isVisible());
    assert.equal(await page.locator('.game').count(),0);
    await page.locator('#sync-provider').selectOption('drive');
    try {
      await page.locator('#drive-setup').waitFor({state:'visible'});
      const configured=process.env.EMUSYNC_EXPECT_DRIVE_CONFIGURED==='1';
      assert.equal(await page.locator('#drive-help').isVisible(),!configured,'Setup guidance must match the packaged Google configuration');
      assert.equal(await page.locator('#drive-connect').isEnabled(),configured,'Google consent must be available only in configured builds');
      assert.equal(await page.locator('#drive-disconnect').isVisible(),configured,'Reset must be available even before account verification succeeds');
      if(configured) assert.equal(await page.locator('#drive-disconnect').innerText(),'Reset connection');
      assert(await page.locator('#host-pairing').isHidden());
    } finally {
      await page.locator('#sync-provider').selectOption('wifi');
      await page.locator('#host-pairing').waitFor({state:'visible'});
    }
    await page.screenshot({path:require('node:path').resolve(__dirname,'../dist/native-windows.png'),fullPage:true});
    console.log('PASS: published Windows startup, real native setup bridge, Drive mode/configuration gating, and switching back to Wi-Fi.');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
