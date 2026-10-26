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
          if(command==='drive-connect' && window.failConnect) { window.failConnect=false; return JSON.stringify({state,error:'Test permission denied'}); }
          if(command==='games-folder') { state.gamesFolder='Test games'; state.games=[{id:'ULUS12345',title:'Test <img src=x onerror=alert(1)>',size:1024,icon:null,background:null},{id:'ULUS99999',title:'Second Game',size:2048}]; state.installed=['ULUS12345']; state.selected='ULUS12345'; }
          if(command==='psp-folder') state.pspFolder='Test PSP';
          if(command==='emulator') state.emulator='Test PPSSPP';
          if(command==='select') state.selected=value;
          if(command==='play') state.status='Sync completed before launch';
          if(command==='finish-session') state.session=null;
          if(command==='sync-provider') state.syncProvider=value;
          if(command==='drive-connect') { state.driveConnected=true; state.driveEmail='test@example.invalid'; }
          if(command==='drive-disconnect') { state.driveConnected=false; state.driveEmail=null; }
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
    await page.locator('[data-command=get-ppsspp]').click();
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='get-ppsspp').length),1);
    await page.locator('#close-setup').click();
    await page.locator('#add-game').click();
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='add-game').length),1);
    assert.equal(await page.locator('.game img').count(),0,'Game title must not become HTML');
    await page.locator('.game').nth(1).click();
    assert.equal(await page.locator('#game-title').innerText(),'Second Game');
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='play').length),0,'Selection must not sync');
    assert(await page.locator('#library-browse').isHidden(),'Selecting a game opens its detail view');
    await page.locator('#play').click();
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='play').length),1);
    assert.equal(await page.locator('#status').innerText(),'Sync completed before launch');
    await page.locator('#settings').click();
    await page.locator('#sync-provider').selectOption('drive');
    await page.locator('#drive-setup').waitFor({state:'visible'});
    assert(await page.locator('#phone-pairing').isHidden());
    assert(await page.locator('#host-pairing').isHidden());
    assert(await page.locator('#drive-disconnect').isVisible(),'Failed/unverified sign-in must still allow resetting cached credentials');
    assert.equal(await page.locator('#drive-disconnect').innerText(),'Reset connection');
    await page.locator('#drive-disconnect').click();
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='drive-disconnect').length),1);
    await page.evaluate(()=>window.failConnect=true);
    await page.locator('#drive-connect').click();
    assert(await page.locator('#setup #error').isVisible(),'Settings failures must be visible inside the modal');
    assert(await page.locator('#drive-disconnect').isVisible(),'Failed connection must still allow reset');
    await page.locator('#drive-connect').click();
    await page.waitForFunction(()=>document.getElementById('drive-status').textContent.includes('test@example.invalid'));
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='play').length),1,'Connecting Drive must not sync saves');
    assert.equal(await page.locator('#drive-disconnect').innerText(),'Disconnect');
    await page.evaluate(()=>window.setState({...window.fixtureState,driveConfigured:false}));
    assert(await page.locator('#drive-connect').isDisabled());
    assert(await page.locator('#drive-help').isVisible());
    await page.evaluate(()=>window.setState(window.fixtureState));
    await page.locator('#close-setup').click();
    await page.locator('#header-back').click();
    assert.notEqual(await page.evaluate(()=>document.activeElement.id),'search','Returning to the gallery must not open the mobile keyboard');
    await page.locator('#search').fill('Second');
    assert.equal(await page.locator('.game').count(),1,'Search filters the library without native calls');
    await page.locator('#search').fill('missing game');
    assert(await page.locator('#no-results').isVisible());
    await page.locator('#clear-search').click();
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
      await page.waitForFunction(()=>document.activeElement.id==='close-setup');
      await page.keyboard.press('Shift+Tab');
      assert(await page.evaluate(()=>document.getElementById('setup').contains(document.activeElement)),'Keyboard focus must stay inside Settings');
      await page.locator('#close-setup').click();
      await page.locator('#header-back').click();
      assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`Gallery overflow at ${width}`);
      await page.locator('.game').first().click();
    }
    assert.deepEqual(errors,[]);
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-desktop.png'),fullPage:true});
    await page.setViewportSize({width:390,height:900});
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-android.png'),fullPage:true});
    await page.evaluate(()=>{
      const titles=['Monster Hunter Freedom Unite','Persona 3 Portable','WipEout Pure','God of War: Ghost of Sparta','Gran Turismo','Lumines','Crisis Core: Final Fantasy VII','Patapon'];
      const colors=[['#83ada5','#233c38'],['#7868aa','#18132f'],['#72b8d2','#18334c'],['#d18b63','#46271f'],['#75a0bb','#253945'],['#b4ac6b','#433e24'],['#8ea7c3','#2c3748'],['#d9b086','#53402f']];
      const art=(index,logo=false)=>{
        const canvas=document.createElement('canvas'); canvas.width=logo?500:900; canvas.height=logo?240:520;
        const ctx=canvas.getContext('2d'),w=canvas.width,h=canvas.height;
        if(logo) { ctx.fillStyle='white'; ctx.textAlign='center'; ctx.font='600 44px Bahnschrift, sans-serif'; const words=titles[index].split(' '); const middle=Math.ceil(words.length/2); ctx.fillText(words.slice(0,middle).join(' '),w/2,100,460); ctx.fillText(words.slice(middle).join(' '),w/2,154,460); }
        else { const gradient=ctx.createLinearGradient(0,0,w,h); gradient.addColorStop(0,colors[index][0]); gradient.addColorStop(1,colors[index][1]); ctx.fillStyle=gradient; ctx.fillRect(0,0,w,h); ctx.fillStyle='#ffffff33'; ctx.beginPath(); ctx.arc(w*.7,h*.3,65,0,Math.PI*2); ctx.fill(); for(let layer=0;layer<4;layer++) { ctx.fillStyle=['#25344844','#1d2d3e66','#12233699','#101e2cdd'][layer]; ctx.beginPath(); ctx.moveTo(0,h); for(let x=0;x<=w;x+=45) ctx.lineTo(x,h*(.5+layer*.12)-Math.abs(Math.sin(x*.008+index+layer))*h*.2); ctx.lineTo(w,h); ctx.fill(); } }
        return canvas.toDataURL('image/png');
      };
      Object.assign(window.fixtureState,{platform:'windows',games:titles.map((title,index)=>({id:'ULUS'+String(12000+index),title,size:(700+index*90)*1024*1024,icon:art(index,true),background:art(index)})),selected:'ULUS12000',installed:['ULUS12000','ULUS12001','ULUS12003','ULUS12004','ULUS12006'],status:'Choose a game, then press Play.'});
      window.setState(window.fixtureState);
    });
    await page.setViewportSize({width:1440,height:1000});
    await page.locator('#header-back').click();
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-library-desktop.png'),fullPage:true});
    await page.locator('.game').first().click();
    assert(await page.locator('#detail-art').isHidden(),'A full banner uses the ISO background without duplicating its icon');
    await page.locator('#game-options').evaluate(element=>element.open=false);
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-game-desktop.png'),fullPage:true});
    await page.setViewportSize({width:390,height:844});
    const playBox=await page.locator('#play').boundingBox();
    assert(playBox.height>=48 && playBox.y+playBox.height<=844,'Mobile Play stays within the viewport with a large touch target');
    assert.equal(await page.getByRole('button',{name:'Settings',exact:true}).count(),1,'Icon-only mobile Settings retains an accessible name');
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-game-mobile.png'),fullPage:true});
    await page.locator('#header-back').click();
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-library-mobile.png'),fullPage:true});
    await page.evaluate(()=>window.scrollTo(0,500));
    assert((await page.locator('.topbar').boundingBox()).y===0,'Mobile header stays visible while the library scrolls');
    await page.locator('.game').first().click();
    await page.locator('#settings').click();
    await page.locator('.settings-panel').evaluate(element=>element.scrollTop=500);
    const heading=await page.locator('.setup-heading').boundingBox();
    assert(heading.y>=0 && heading.y<10,'Settings keeps its heading and Done button visible');
    assert(await page.evaluate(()=>window.navigateBack()),'Device Back closes Settings');
    assert(await page.locator('#setup').isHidden());
    assert(await page.locator('#selected').isVisible(),'Closing Settings preserves the selected game');
    assert(await page.evaluate(()=>window.navigateBack()),'Device Back returns from a game to the library');
    assert(await page.locator('#library-browse').isVisible());
    assert.equal(await page.evaluate(()=>window.navigateBack()),false,'At the library root Back can leave the app');
    await page.evaluate(()=>{ Object.assign(window.fixtureState,{platform:'android',session:window.fixtureState.selected}); window.setState(window.fixtureState); });
    await page.locator('.game').first().click();
    assert(await page.locator('#play').isDisabled());
    assert(await page.locator('#add-game').isDisabled());
    await page.locator('#finish-session').click();
    assert.equal(await page.evaluate(()=>calls.filter(c=>c.command==='finish-session').length),1,'Android offers a retry for unfinished session sync');
    assert(await page.locator('#finish-session').isHidden());
    await page.evaluate(()=>window.setGameSync(window.fixtureState.selected,{state:'syncing',message:'Uploading save · 37%',percent:37,lastSynced:'2026-10-25T12:00:00Z'}));
    assert.equal(await page.locator('#save-progress').getAttribute('value'),'37');
    assert.equal(await page.locator('#save-phase').innerText(),'Syncing save');
    assert((await page.locator('#save-last').innerText()).includes('2026'),'Last successful sync survives an in-progress transfer');
    await page.evaluate(()=>window.setGameSync(window.fixtureState.selected,{state:'playing',message:'Playing · Sync after exit',lastSynced:'2026-10-25T12:00:00Z'}));
    assert(await page.locator('#save-progress').isHidden());
    assert.equal(await page.locator('#save-phase').innerText(),'Playing · Sync after exit');
    await page.evaluate(()=>window.setGameSync(window.fixtureState.selected,{state:'synced',message:'Safe to switch devices',lastSynced:'2026-10-26T12:00:00Z'}));
    assert.equal(await page.locator('#save-phase').innerText(),'Safe to switch devices');
    await page.locator('#header-back').click();
    assert((await page.locator('.save-badge').allTextContents()).includes('Safe to switch devices'),'Library cards reflect per-game sync state');
    await page.locator('.game').first().click();
    await page.evaluate(()=>window.setGameSync(window.fixtureState.selected,{state:'pending',message:'Offline · Progress is still on this device',lastSynced:'2026-10-26T12:00:00Z'}));
    assert.equal(await page.locator('#save-phase').innerText(),'Sync needed');
    await page.evaluate(()=>window.setGameSync(window.fixtureState.games[1].id,{state:'synced',message:'Safe to switch devices'}));
    assert.equal(await page.locator('#save-phase').innerText(),'Sync needed','A different game finishing must not change the selected game status');
    await page.screenshot({path:path.resolve(__dirname,'../dist/ui-sync-mobile.png'),fullPage:true});
    assert.deepEqual(errors,[]);
    console.log('PASS: legacy WebView support, safe titles/artwork, gallery/detail navigation, search, sync only on Play, Drive controls, modal error visibility/focus, and mobile layouts.');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
