(() => {
  const byId=id=>document.getElementById(id);
  let state=null,busy=false,detailOpen=false,returnFocus=null;
  const image=value=>typeof value==='string' && /^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(value)?value:null;
  const tones=['#35566c','#58456c','#38645c','#73513d','#3b506f','#5e6040'];
  window.setStatus=message=>{ byId('status').textContent=message; byId('library-status').textContent=message; byId('setup-status').textContent=message; };
  function buttons() {
    document.querySelectorAll('button,select,#sync-game').forEach(button=>button.disabled=busy);
    byId('drive-connect').disabled=busy || !state?.driveConfigured;
    byId('play').disabled=busy || !state?.selected || !!state?.session || state?.syncProvider==='drive' && (!state.driveConnected || !state.driveConfigured);
    byId('upload-game').disabled=busy || !state?.driveConnected || !state?.driveConfigured;
    document.body.classList.toggle('busy',busy);
    byId('setup-status').hidden=!busy;
  }
  function views() {
    const selected=state?.games.find(game=>game.id===state.selected);
    const detail=!!selected && detailOpen;
    byId('selected').hidden=!detail;
    byId('library-browse').hidden=detail;
    document.body.classList.toggle('detail-open',detail);
    byId('library-nav').setAttribute('aria-current','page');
  }
  function showLibrary() { detailOpen=false; views(); window.scrollTo(0,0); byId('library-nav').focus(); }
  function setup(open) {
    if(open && byId('setup').hidden) returnFocus=document.activeElement;
    byId('setup').hidden=!open;
    document.body.classList.toggle('setup-open',open);
    [document.querySelector('.topbar'),byId('main-content'),byId('connection')].forEach(element=>{
      if(open) element.setAttribute('aria-hidden','true'); else element.removeAttribute('aria-hidden');
    });
    byId(open?'setup-error-slot':'feedback-slot').append(byId('error'));
    if(open) requestAnimationFrame(()=>{ if(!byId('setup').hidden) byId('close-setup').focus(); });
    else if(returnFocus && document.contains(returnFocus)) returnFocus.focus();
  }
  function renderGames() {
    const query=byId('search').value.trim().toLocaleLowerCase();
    const games=byId('games'); games.textContent='';
    const matches=state.games.filter(game=>game.title.toLocaleLowerCase().includes(query));
    for(const game of matches) {
      const installed=state.installed.includes(game.id);
      const button=document.createElement('button'); button.type='button'; button.className='game'+(installed?' installed':'');
      button.setAttribute('aria-pressed',String(game.id===state.selected));
      const cover=document.createElement('span'); cover.className='cover';
      const tone=tones[Array.from(game.id).reduce((sum,letter)=>sum+letter.charCodeAt(0),0)%tones.length];
      cover.style.backgroundImage=`linear-gradient(145deg,${tone},#18232f)`;
      const background=image(game.background);
      if(background) { const back=document.createElement('img'); back.className='cover-backdrop'; back.src=background; back.alt=''; back.loading='lazy'; cover.append(back); }
      const tag=document.createElement('span'); tag.className='cover-console'; tag.textContent='PSP'; tag.setAttribute('aria-hidden','true'); cover.append(tag);
      const art=image(game.icon);
      const logo=document.createElement(art?'img':'span'); logo.className='thumb'+(art?'':' fallback');
      if(art) { logo.src=art; logo.alt=''; logo.loading='lazy'; } else { logo.textContent=game.title.slice(0,2).toUpperCase(); logo.setAttribute('aria-hidden','true'); }
      cover.append(logo);
      if(!installed) { const download=document.createElement('span'); download.className='cover-download'; download.textContent='↓'; download.setAttribute('aria-hidden','true'); cover.append(download); }
      const title=document.createElement('strong'),subtitle=document.createElement('small');
      title.textContent=game.title; subtitle.textContent=installed?'On this device':state.syncProvider==='drive'?'Download from Drive':'Download from PC';
      button.append(cover,title,subtitle);
      button.addEventListener('click',async()=>{ if(await call('select',game.id)) { detailOpen=true; views(); window.scrollTo(0,0); byId('back-library').focus(); } });
      games.append(button);
    }
    byId('empty').hidden=state.games.length!==0;
    byId('no-results').hidden=state.games.length===0 || matches.length!==0;
    byId('game-count').textContent=state.games.length+' '+(state.games.length===1?'game':'games');
    document.querySelector('.search-box').hidden=state.games.length===0;
    byId('choose-label').textContent=query?'Search results':'All games';
    buttons();
  }
  window.setState=value=>{
    state=value;
    const drive=state.syncProvider==='drive';
    renderGames();
    const selected=state.games.find(game=>game.id===state.selected);
    if(!selected) detailOpen=false;
    if(selected) {
      const installed=state.installed.includes(selected.id);
      byId('play').textContent=installed?'▶   Play':'↓   Download & Play';
      byId('game-title').textContent=selected.title;
      byId('game-location').textContent=installed?'On this device':drive?'Available on Google Drive':'Available on your PC';
      const background=image(selected.background),icon=image(selected.icon);
      byId('hero').style.backgroundImage=background?`url("${background}")`:'linear-gradient(125deg,#2d4960,#152330 75%)';
      byId('detail-art').hidden=!!background || !icon;
      if(icon) byId('detail-art').src=icon; else byId('detail-art').removeAttribute('src');
      const megabytes=selected.size/1024/1024;
      byId('game-size').textContent=(megabytes>=1024?(megabytes/1024).toFixed(1)+' GB':Math.max(1,Math.round(megabytes))+' MB')+' · ISO';
      byId('sync-game').checked=state.syncGames[selected.id]!==false;
    }
    byId('games-path').textContent=state.gamesFolder||'Not selected';
    byId('psp-path').textContent=state.pspFolder||'Select the PSP folder used by PPSSPP.';
    byId('emulator-path').textContent=state.emulator||'Not selected';
    byId('emulator-button').textContent=state.platform==='android'?'Detect PPSSPP':'Choose PPSSPP';
    byId('sync-provider').value=drive?'drive':'wifi';
    byId('drive-setup').hidden=!drive;
    byId('phone-pairing').hidden=drive;
    byId('host-pairing').hidden=drive || state.platform!=='windows';
    byId('drive-status').textContent=!state.driveConfigured?'Google Drive needs one-time app setup before sign-in is available.':state.driveConnected?'Connected as '+state.driveEmail:'Connect the same Google account on both devices.';
    byId('drive-help').hidden=!!state.driveConfigured;
    byId('drive-disconnect').hidden=!state.driveConfigured;
    byId('drive-disconnect').textContent=state.driveConnected?'Disconnect':'Reset connection';
    byId('drive-connect').textContent=state.driveConnected?'Reconnect Google Drive':'Connect Google Drive';
    byId('play-note').textContent=drive && (!state.driveConnected || !state.driveConfigured)?'Connect Google Drive in Settings before playing with cloud sync.':'Your save is checked before PPSSPP opens. Different progress asks first.';
    byId('sync-caption').textContent=(drive?'Google Drive':'Same Wi-Fi')+' · Check on Play';
    byId('upload-game').hidden=!drive || !selected || !state.installed.includes(selected.id);
    byId('download-label').textContent=drive?'Download this game from Drive when needed':'Download this game from PC when needed';
    byId('empty').querySelector('p').textContent=drive?'Add a local games folder, then connect Google Drive to find your uploaded games.':'Add a games folder, or connect your main PC to find games you can download.';
    byId('pair-title').textContent=state.platform==='windows'?'Connect to another PC':'Connect your PC';
    byId('pair-status').textContent=state.paired?'Main PC connected. Paste a new code here if pairing changes.':'Copy a pairing code from the main PC, then paste it here.';
    byId('backup-path').textContent='Backups: '+state.backups;
    const warnings=byId('warnings'); warnings.textContent='';
    warnings.append(...state.warnings.map(message=>{ const li=document.createElement('li'); li.textContent=message; return li; }));
    byId('connection').textContent=drive?'Google Drive · Finish syncing before switching devices':state.platform==='windows'?'Same Wi-Fi · Keep EmuSync open for your other devices':'Same Wi-Fi · Connect to the same network as your PC';
    byId('connection-name').textContent=drive?(state.driveConnected?'Drive connected':'Connect Drive'):'Same Wi-Fi';
    byId('connection-chip').classList.toggle('connected',drive && !!state.driveConnected);
    window.setStatus(state.status); views(); buttons();
  };
  async function call(command,value='') {
    if(busy) return false;
    busy=true; buttons(); byId('error').hidden=true;
    try {
      const raw=await window.HybridWebView.InvokeDotNet('Invoke',[command,value]);
      const result=typeof raw==='string'?JSON.parse(raw):raw;
      if(result.state) window.setState(result.state);
      if(result.error) { byId('error').textContent=result.error; byId('error').hidden=false; }
      if(command==='paste-pair') byId('pair-code').value=result.code||'';
      if(command==='load' && state && (!state.gamesFolder||!state.pspFolder||!state.emulator)) setup(true);
      return !result.error;
    } catch(error) { byId('error').textContent='Could not complete this action. '+error.message; byId('error').hidden=false; return false; }
    finally { busy=false; buttons(); }
  }
  byId('settings').addEventListener('click',()=>setup(true));
  byId('empty-settings').addEventListener('click',()=>setup(true));
  byId('connection-chip').addEventListener('click',()=>setup(true));
  byId('close-setup').addEventListener('click',()=>setup(false));
  byId('setup').addEventListener('click',event=>{ if(event.target===byId('setup') && !busy) setup(false); });
  document.addEventListener('keydown',event=>{
    if(byId('setup').hidden) return;
    if(event.key==='Escape' && !busy) { event.preventDefault(); setup(false); }
    if(event.key==='Tab') {
      const items=Array.from(byId('setup').querySelectorAll('button,input,textarea,select,summary')).filter(item=>!item.disabled && item.getClientRects().length);
      const first=items[0],last=items[items.length-1];
      if(!first) { event.preventDefault(); return; }
      if(event.shiftKey && (document.activeElement===first || !byId('setup').contains(document.activeElement))) { event.preventDefault(); last.focus(); }
      else if(!event.shiftKey && (document.activeElement===last || !byId('setup').contains(document.activeElement))) { event.preventDefault(); first.focus(); }
    }
  });
  byId('back-library').addEventListener('click',showLibrary);
  byId('library-nav').addEventListener('click',showLibrary);
  byId('search').addEventListener('input',()=>{ if(state) renderGames(); });
  byId('clear-search').addEventListener('click',()=>{ byId('search').value=''; renderGames(); byId('search').focus(); });
  byId('play').addEventListener('click',()=>call('play',state.selected));
  byId('sync-game').addEventListener('change',()=>call('sync-game',JSON.stringify({id:state.selected,enabled:byId('sync-game').checked})));
  document.querySelectorAll('[data-command]').forEach(button=>button.addEventListener('click',()=>call(button.dataset.command)));
  byId('paste').addEventListener('click',()=>call('paste-pair'));
  byId('connect').addEventListener('click',()=>call('pair',byId('pair-code').value));
  byId('sync-provider').addEventListener('change',()=>call('sync-provider',byId('sync-provider').value));
  byId('upload-game').addEventListener('click',()=>call('upload-game',state.selected));
  call('load');
})();
