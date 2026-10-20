const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'C:/Users/Guroc/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
  const root=path.resolve(__dirname,'../src/EmuSync.App/Resources/Raw/wwwroot');
  const browser=await chromium.launch({headless:true,channel:'msedge'});
  try {
    const page=await browser.newPage(); const errors=[];
    await page.addInitScript(()=>{ delete Element.prototype.replaceChildren; });
    page.on('pageerror',error=>errors.push(error.message));
    await page.route('https://app.test/**',route=>{
      const name=new URL(route.request().url()).pathname.slice(1)||'index.html';
      if(name==='_framework/hybridwebview.js') return route.fulfill({contentType:'text/javascript',body:`
        window.calls=[];
        const state=window.fixtureState={platform:'windows',syncProvider:'wifi',driveConfigured:true,games:[],selected:null,installed:[],syncGames:{},warnings:[],status:'Choose a game',backups:'Test backups'};
        window.HybridWebView={InvokeDotNet:async(_, [command,value])=>{
          window.calls.push({command,value});
          if(command==='games-folder') { state.gamesFolder='Test games'; state.games=[{id:'ULUS12345',title:'Test <img src=x onerror=alert(1)>',size:1024,icon:null,background:null},{id:'ULUS99999',title:'Second Game',size:2048}]; state.installed=['ULUS12345']; state.selected='ULUS12345'; }
          if(command==='psp-folder') state.pspFolder='Test PSP';
          if(command==='emulator') state.emulator='Test PPSSPP';
          if(command==='select') state.selected=value;
          if(command==='play') state.status='Sync completed before launch';
          if(command==='sync-provider') state.syncProvider=value;
          if(command==='drive-connect') { state.driveConnected=true; state.driveEmail='test@example.invalid'; }
          if(command==='upload-game') state.status='Game available on Google Drive';
          if(command==='sync-game') { const option=JSON.parse(value); state.syncGames[option.id]=option.enabled; }
          return JSON.stringify({state});
        }};
      `});
      if(!['index.html','app.css','app.js'].includes(name)) return route.fulfill({status:404,body:''});
      return route.fulfill({body:fs.readFileSync(path.join(root,name)),contentType:name.endsWith('.css')?'text/css':name.endsWith('.js')?'text/javascript':'text/html'});
    });
    await page.goto('https://app.test/');
    await page.locator('#setup').waitFor({state:'visible'});
    assert.equal(await page.evaluate(()=>typeof document.body.replaceChildren),'undefined','Exercise older Android WebViews');
    assert.equal(await page.evaluate(()=>calls.some(c=>c.command==='play')),false);
    await page.locator('[data-command=games-folder]').click();
    await page.locator('[data-command=psp-folder]').click();
    await page.locator('[data-command=emulator]').click();
    await page.locator('#close-setup').click();
    assert.equal(await page.locator('.game img').count(),0,'Game title must not become HTML');
    await page.locator('.game').nth(1).click();
    assert.equal(await page.locator('#game-title').innerText(),'Second Game');
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='play').length),0,'Selection must not sync');
    await page.locator('#play').click();
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='play').length),1);
    assert.equal(await page.locator('#status').innerText(),'Sync completed before launch');
    await page.locator('#settings').click();
    await page.locator('#sync-provider').selectOption('drive');
    await page.locator('#drive-setup').waitFor({state:'visible'});
    assert(await page.locator('#phone-pairing').isHidden());
    assert(await page.locator('#host-pairing').isHidden());
    await page.locator('#drive-connect').click();
    await page.waitForFunction(()=>document.getElementById('drive-status').textContent.includes('test@example.invalid'));
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='play').length),1,'Connecting Drive must not sync saves');
    await page.evaluate(()=>window.setState({...window.fixtureState,driveConfigured:false}));
    assert(await page.locator('#drive-connect').isDisabled());
    assert(await page.locator('#drive-help').isVisible());
    await page.evaluate(()=>window.setState(window.fixtureState));
    await page.locator('#close-setup').click();
    await page.locator('.game').first().click();
    await page.locator('#selected summary').click();
    await page.locator('#upload-game').click();
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='upload-game').length),1);
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='play').length),1,'Optional ISO upload must not launch or sync saves');
    for(const width of [320,390,736,1024]) {
      await page.setViewportSize({width,height:900});
      assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`Library overflow at ${width}`);
      await page.locator('#settings').click();
      assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`Setup overflow at ${width}`);
      await page.locator('#close-setup').click();
    }
    assert.deepEqual(errors,[]);
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-desktop.png'),fullPage:true});
    await page.setViewportSize({width:390,height:900});
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-android.png'),fullPage:true});
    console.log('PASS: legacy WebView support, safe game titles, selection without sync, Play, Drive setup/upload controls, missing OAuth setup, and responsive layout.');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
