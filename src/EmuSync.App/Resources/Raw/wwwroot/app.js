(() => {
  const byId=id=>document.getElementById(id);
  let state=null,busy=false;
  const image=value=>typeof value==='string' && /^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(value)?value:null;
  window.setStatus=message=>{ byId('status').textContent=message; };
  function buttons() {
    document.querySelectorAll('button').forEach(button=>button.disabled=busy);
    byId('play').disabled=busy || !state?.selected || !!state?.session;
    document.body.classList.toggle('busy',busy);
  }
  window.setState=value=>{
    state=value;
    const games=byId('games'); games.textContent='';
    for(const game of state.games) {
      const button=document.createElement('button'); button.type='button'; button.className='game';
      button.setAttribute('aria-pressed',String(game.id===state.selected));
      const art=image(game.icon);
      const cover=document.createElement(art?'img':'span'); cover.className='thumb'+(art?'':' fallback');
      if(art) { cover.src=art; cover.alt=''; } else { cover.textContent=game.title.slice(0,2).toUpperCase(); cover.setAttribute('aria-hidden','true'); }
      const text=document.createElement('span'),title=document.createElement('strong'),subtitle=document.createElement('small');
      title.textContent=game.title; subtitle.textContent=state.installed.includes(game.id)?'On this device':'Download from PC';
      text.append(title,subtitle); button.append(cover,text);
      button.addEventListener('click',()=>call('select',game.id)); games.append(button);
    }
    const selected=state.games.find(game=>game.id===state.selected);
    byId('selected').hidden=!selected; byId('empty').hidden=!!selected; byId('choose-label').hidden=!selected;
    if(selected) {
      byId('game-title').textContent=selected.title;
      const background=image(selected.background);
      byId('hero').style.backgroundImage=background?`linear-gradient(0deg,#171d25ed,#171d2510),url("${background}")`:'linear-gradient(150deg,#284758,#171d25)';
      byId('game-size').textContent=`${(selected.size/1024/1024).toFixed(0)} MB · ISO`;
      byId('sync-game').checked=state.syncGames[selected.id]!==false;
    }
    byId('games-path').textContent=state.gamesFolder||'Not selected';
    byId('psp-path').textContent=state.pspFolder||'Select the PSP folder used by PPSSPP.';
    byId('emulator-path').textContent=state.emulator||'Not selected';
    byId('emulator-button').textContent=state.platform==='android'?'Detect PPSSPP':'Choose PPSSPP';
    byId('host-pairing').hidden=state.platform!=='windows'; byId('phone-pairing').hidden=state.platform!=='android';
    byId('pair-status').textContent=state.paired?'PC connected. Paste a new code here if pairing changes.':'Copy a pairing code from the PC app, then paste it here.';
    byId('backup-path').textContent='Backups: '+state.backups;
    const warnings=byId('warnings'); warnings.textContent='';
    warnings.append(...state.warnings.map(message=>{ const li=document.createElement('li'); li.textContent=message; return li; }));
    byId('connection').textContent=state.platform==='windows'?'Keep this app open for Android · Same Wi-Fi':'Connect to the same Wi-Fi as your PC';
    window.setStatus(state.status); buttons();
  };
  async function call(command,value='') {
    if(busy) return;
    busy=true; buttons(); byId('error').hidden=true;
    try {
      const raw=await window.HybridWebView.InvokeDotNet('Invoke',[command,value]);
      const result=typeof raw==='string'?JSON.parse(raw):raw;
      if(result.state) window.setState(result.state);
      if(result.error) { byId('error').textContent=result.error; byId('error').hidden=false; }
      if(command==='paste-pair') byId('pair-code').value=result.code||'';
      if(command==='load' && state && (!state.gamesFolder||!state.pspFolder||!state.emulator)) byId('setup').hidden=false;
    } catch(error) { byId('error').textContent='Could not complete this action. '+error.message; byId('error').hidden=false; }
    finally { busy=false; buttons(); }
  }
  byId('settings').addEventListener('click',()=>byId('setup').hidden=!byId('setup').hidden);
  byId('close-setup').addEventListener('click',()=>byId('setup').hidden=true);
  byId('play').addEventListener('click',()=>call('play',state.selected));
  byId('sync-game').addEventListener('change',()=>call('sync-game',JSON.stringify({id:state.selected,enabled:byId('sync-game').checked})));
  document.querySelectorAll('[data-command]').forEach(button=>button.addEventListener('click',()=>call(button.dataset.command)));
  byId('paste').addEventListener('click',()=>call('paste-pair'));
  byId('connect').addEventListener('click',()=>call('pair',byId('pair-code').value));
  call('load');
})();
