import { useState } from 'react';
import Quicker from 'Quicker';

const RESERVED = ['system', 'idle', 'registry', 'smss', 'csrss', 'wininit', 'winlogon', 'services', 'lsass', 'svchost', 'dwm', 'fontdrvhost', 'sihost', 'taskhostw', 'ctfmon', 'explorer', 'conhost', 'powershell', 'pwsh', 'cmd', 'wscript', 'cscript', 'mshta', 'rundll32', 'regsvr32', 'spoolsv', 'audiodg', 'searchindexer', 'trustedinstaller', 'searchhost', 'startmenuexperiencehost', 'shellexperiencehost', 'textinputhost', 'applicationframehost', 'lockapp', 'smartscreen', 'securityhealthservice', 'msmpeng', 'nissrv', 'backgroundtaskhost', 'systemsettings', 'searchapp', 'runtimebroker', 'dllhost', 'taskhost', 'searchprotocolhost', 'searchfilterhost', 'widgetservice'];

function splitNames(value) {
  return String(value || '').split(/[;；,，\s]+/).map(function (x) { return x.trim(); }).filter(Boolean);
}

function cleanName(value) {
  return String(value || '').trim().replace(/\.exe$/i, '');
}

function formatNumber(value) {
  const n = Number(value);
  if (!isFinite(n)) return '0';
  return String(Math.round(n * 10) / 10);
}

function initials(name) {
  const s = String(name || '').trim();
  if (!s) return '新';
  if (/^[\u4e00-\u9fa5]/.test(s)) return s.slice(0, 1);
  const parts = s.split(/[\s_-]+/).filter(Boolean);
  if (parts.length > 1) return (parts[0].slice(0, 1) + parts[1].slice(0, 1)).toUpperCase();
  return s.slice(0, 2).toUpperCase();
}

function tintIndex(name) {
  const s = String(name || '');
  let h = 0;
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) % 997;
  return h % 6;
}

function brief(value, limit) {
  const s = String(value || '').trim();
  return s.length > limit ? s.slice(0, limit) + '…' : s;
}

function procSummary(procs) {
  if (!procs.length) return '未设置';
  if (procs.length <= 2) return procs.join('、');
  return procs.slice(0, 2).join('、') + ' 等 ' + procs.length + ' 个';
}

function transferSummary(app) {
  const list = splitNames(app.transferProcesses).map(cleanName);
  if (list.length === 0) return '全部进程';
  if (list.length <= 2) return list.join('、');
  return list.slice(0, 2).join('、') + ' 等 ' + list.length + ' 个';
}

function isReserved(name) {
  const n = cleanName(name).toLowerCase();
  if (!n) return true;
  if (n.indexOf('quicker') === 0) return true;
  return RESERVED.indexOf(n) >= 0;
}

const css = `
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
.app {
  width: 1040px; height: 720px; margin: 0 auto; display: flex; flex-direction: column;
  --bg: #f3f6fb; --card: #ffffff; --soft: #f6f8fc; --text: #1b2230; --muted: #6b7688;
  --line: #e3e8f1; --line-strong: #d3dbe8; --accent: #2f6fed; --accent-weak: #eaf1ff;
  --green: #1f9d63; --green-weak: #e5f6ee; --danger: #cf4436; --shadow: 0 10px 30px rgba(23, 43, 77, 0.06);
  background: radial-gradient(900px 380px at 92% -140px, rgba(47, 111, 237, 0.10), rgba(47, 111, 237, 0) 70%), var(--bg);
  color: var(--text); font-size: 16px; line-height: 1.5;
  font-family: 'Segoe UI', 'Microsoft YaHei UI', 'Microsoft YaHei', sans-serif;
  overflow: hidden;
}
.app.dark {
  --bg: #12151b; --card: #1a1f27; --soft: #151a21; --text: #eef2f8; --muted: #98a2b3;
  --line: #29303b; --line-strong: #333c49; --accent: #79a3ff; --accent-weak: #1b2842;
  --green: #54cf97; --green-weak: #163328; --danger: #ff8177; --shadow: none;
  background: radial-gradient(900px 380px at 92% -140px, rgba(109, 155, 255, 0.12), rgba(109, 155, 255, 0) 70%), var(--bg);
}
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: 20px; padding: 22px 28px 10px; }
h1 { margin: 0; font-size: 24px; line-height: 1.3; font-weight: 650; letter-spacing: 0.2px; }
.sub { margin: 5px 0 0; color: var(--muted); font-size: 14.5px; }
.pill { display: inline-flex; align-items: center; gap: 8px; padding: 7px 13px; border-radius: 999px; border: 1px solid var(--line); color: var(--muted); background: var(--card); font-size: 13.5px; white-space: nowrap; box-shadow: var(--shadow); }
.pill-on { color: var(--green); background: var(--green-weak); border-color: transparent; box-shadow: none; }
.dot { width: 8px; height: 8px; border-radius: 50%; background: #aab3c2; display: inline-block; flex: none; }
.dot-on { background: var(--green); }
.toolbar { display: flex; align-items: center; gap: 12px; padding: 6px 28px 12px; }
.search { position: relative; flex: 1; max-width: 440px; }
.search-icon { position: absolute; left: 13px; top: 50%; width: 17px; height: 17px; margin-top: -8.5px; color: var(--muted); pointer-events: none; opacity: 0.8; }
.search input { padding-left: 38px; }
input {
  width: 100%; border: 1px solid var(--line); border-radius: 10px; background: var(--card);
  color: var(--text); padding: 9px 12px; font-size: 15px; outline: none; font-family: inherit;
  transition: border-color 0.15s, box-shadow 0.15s;
}
input::placeholder { color: var(--muted); opacity: 0.72; }
input:focus { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-weak); }
input[type=number] { appearance: textfield; -moz-appearance: textfield; }
.btn {
  border: 1px solid var(--line); background: var(--card); color: var(--text);
  border-radius: 10px; padding: 9px 15px; font-size: 14.5px; cursor: pointer; white-space: nowrap;
  font-family: inherit; height: auto; min-width: 0; transition: background 0.15s, border-color 0.15s, color 0.15s;
}
.btn:hover { border-color: var(--line-strong); background: var(--soft); color: var(--text); }
.btn-primary { background: var(--accent); border-color: var(--accent); color: #ffffff; font-weight: 600; padding: 9px 20px; }
.btn-primary:hover { background: var(--accent); border-color: var(--accent); color: #ffffff; filter: brightness(1.07); }
.btn-add { background: var(--accent-weak); border-color: transparent; color: var(--accent); font-weight: 600; margin-left: auto; }
.btn-add:hover { background: var(--accent-weak); border-color: transparent; color: var(--accent); filter: brightness(0.98); }
.plus { font-size: 16px; font-weight: 600; margin-right: 3px; }
.btn-ghost { border-color: transparent; background: transparent; color: var(--muted); padding: 5px 9px; font-size: 13px; border-radius: 8px; }
.btn-ghost:hover { border-color: transparent; color: var(--accent); background: var(--accent-weak); }
.btn-ghost.danger:hover { color: var(--danger); background: rgba(207, 68, 54, 0.10); }
.picker { display: flex; align-items: center; gap: 12px; padding: 0 28px 12px; min-width: 0; }
.picker-label { display: inline-flex; align-items: center; gap: 7px; flex: none; font-size: 13px; color: var(--muted); }
.live { width: 7px; height: 7px; border-radius: 50%; background: var(--green); box-shadow: 0 0 0 3px var(--green-weak); }
.chips { display: flex; align-items: center; gap: 7px; flex: 1; min-width: 0; overflow-x: auto; overflow-y: hidden; padding: 2px 2px 4px; }
.chips::-webkit-scrollbar { height: 4px; }
.chips::-webkit-scrollbar-thumb { background: var(--line-strong); border-radius: 4px; }
.chip { border: 1px solid var(--line); background: var(--card); color: var(--text); border-radius: 999px; padding: 5px 11px; font-size: 13px; cursor: pointer; font-family: inherit; height: auto; min-width: 0; white-space: nowrap; }
.chip:hover { border-color: transparent; color: var(--accent); background: var(--accent-weak); }
.chip .cplus { color: var(--accent); margin-right: 4px; font-weight: 600; }
.picker-empty { color: var(--muted); font-size: 13px; white-space: nowrap; }
.list-panel {
  flex: 1; min-height: 0; margin: 0 28px; display: flex; flex-direction: column;
  background: var(--card); border: 1px solid var(--line); border-radius: 16px;
  box-shadow: var(--shadow); overflow: hidden;
}
.list-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 18px; border-bottom: 1px solid var(--line); }
.list-title { display: flex; align-items: center; gap: 9px; font-size: 14.5px; font-weight: 650; letter-spacing: 0.2px; }
.count { min-width: 22px; height: 22px; padding: 0 7px; border-radius: 999px; background: var(--accent-weak); color: var(--accent); font-size: 12px; font-weight: 650; display: inline-flex; align-items: center; justify-content: center; }
.list-meta { font-size: 12.5px; color: var(--muted); }
.list-body { flex: 1; min-height: 0; overflow-y: auto; }
.list-body::-webkit-scrollbar { width: 8px; }
.list-body::-webkit-scrollbar-thumb { background: var(--line-strong); border-radius: 8px; }
.list-body::-webkit-scrollbar-track { background: transparent; }
.row-wrap { border-bottom: 1px solid var(--line); }
.row-wrap:last-child { border-bottom: none; }
.row { display: flex; align-items: center; gap: 13px; padding: 12px 18px; transition: background 0.15s; }
.row:hover, .row-wrap.open .row { background: var(--soft); }
.avatar { position: relative; flex: none; width: 42px; height: 42px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 15px; font-weight: 700; letter-spacing: 0.4px; }
.badge { position: absolute; right: -3px; bottom: -3px; width: 12px; height: 12px; border-radius: 50%; border: 2px solid var(--card); background: #b3bcc9; }
.badge.live { background: var(--green); }
.tint-0 { background: #e8f0ff; color: #2f6fed; }
.tint-1 { background: #fdeee7; color: #d9663a; }
.tint-2 { background: #e9f7ef; color: #1f9d63; }
.tint-3 { background: #f2ecff; color: #7a5af5; }
.tint-4 { background: #fff5e3; color: #b7791f; }
.tint-5 { background: #e6f6fb; color: #1f7a96; }
.app.dark .tint-0 { background: #1d2b47; color: #8fb2ff; }
.app.dark .tint-1 { background: #3a251d; color: #ffb08a; }
.app.dark .tint-2 { background: #163328; color: #6fdcb0; }
.app.dark .tint-3 { background: #2a2343; color: #b9a3ff; }
.app.dark .tint-4 { background: #3a2f1b; color: #f0c674; }
.app.dark .tint-5 { background: #17313a; color: #7dd3e8; }
.row-main { flex: 1; min-width: 0; cursor: pointer; }
.row-title { display: flex; align-items: center; gap: 8px; min-width: 0; }
.name { font-size: 16px; font-weight: 650; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.state { flex: none; font-size: 11.5px; padding: 1px 7px; border-radius: 999px; color: var(--muted); background: var(--bg); border: 1px solid var(--line); line-height: 1.6; }
.state.on { color: var(--green); background: var(--green-weak); border-color: transparent; }
.state.off { opacity: 0.75; }
.row-sub { margin-top: 3px; color: var(--muted); font-size: 12.5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.tags { margin-top: 6px; display: flex; flex-wrap: wrap; gap: 5px; }
.tag { font-size: 11.5px; line-height: 1.5; color: var(--muted); background: var(--bg); border: 1px solid var(--line); border-radius: 6px; padding: 1px 7px; white-space: nowrap; }
.tag-accent { color: var(--accent); background: var(--accent-weak); border-color: transparent; font-weight: 600; }
.tag-dim { opacity: 0.85; }
.row-actions { display: flex; align-items: center; gap: 10px; flex: none; }
.row-btns { display: flex; align-items: center; gap: 0; }
.switch { position: relative; width: 40px; height: 23px; display: inline-block; flex: none; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider { position: absolute; inset: 0; background: #b9c1ce; border-radius: 999px; transition: background 0.16s; cursor: pointer; }
.slider:before { content: ''; position: absolute; width: 17px; height: 17px; left: 3px; top: 3px; background: #ffffff; border-radius: 50%; transition: transform 0.16s; box-shadow: 0 1px 2px rgba(0, 0, 0, 0.2); }
.switch input:checked + .slider { background: var(--accent); }
.switch input:checked + .slider:before { transform: translateX(17px); }
.app.dark .slider { background: #3b4554; }
.editor { padding: 0 18px 14px 73px; background: var(--soft); animation: rise 0.16s ease-out; }
@keyframes rise { from { opacity: 0; transform: translateY(-3px); } to { opacity: 1; transform: none; } }
.editor-box { border: 1px solid var(--line); background: var(--card); border-radius: 12px; padding: 13px 14px 11px; }
.editor-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 11px 13px; }
.field label { display: block; font-size: 12px; color: var(--muted); margin-bottom: 5px; }
.field label .opt { opacity: 0.7; font-weight: 400; margin-left: 5px; }
.field input { font-size: 14px; padding: 7px 10px; border-radius: 8px; }
.span2 { grid-column: span 2; }
.editor-note { grid-column: span 2; display: flex; align-items: flex-end; padding-bottom: 2px; color: var(--muted); font-size: 12px; line-height: 1.6; }
.empty { padding: 54px 20px 56px; text-align: center; }
.empty-icon { width: 46px; height: 46px; margin: 0 auto 10px; border-radius: 13px; background: var(--soft); border: 1px solid var(--line); color: var(--muted); display: flex; align-items: center; justify-content: center; }
.empty-title { font-size: 15px; font-weight: 600; }
.empty-sub { margin-top: 3px; font-size: 12.5px; color: var(--muted); }
.list-foot { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 18px; border-top: 1px solid var(--line); color: var(--muted); font-size: 12px; }
.foot-right { opacity: 0.9; white-space: nowrap; }
.foot { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 13px 28px 16px; }
.hint { color: var(--muted); font-size: 13px; }
.error { color: var(--danger); font-size: 13px; font-weight: 600; }
.foot-btns { display: flex; gap: 10px; flex: none; }
.muted { color: var(--muted); }
`;

export default function App() {
  const input = Quicker.getInput() || {};
  const theme = (Quicker as any).theme;
  const dark = theme === 'dark' || (theme && theme.mode === 'dark');
  const running = Array.isArray(input.running) ? input.running : [];
  const monitor = input.monitor || {};
  const sourceApps = Array.isArray(input.apps) ? input.apps : [];
  const initial = sourceApps.map(function (a) {
    return {
      name: String(a.name || ''),
      enabled: a.enabled !== false,
      processes: Array.isArray(a.processes) ? a.processes.join('; ') : String(a.processes || ''),
      transferProcesses: Array.isArray(a.transferProcesses) ? a.transferProcesses.join('; ') : String(a.transferProcesses || ''),
      transferCommandLine: String(a.transferCommandLine || ''),
      idleMinutes: Number(a.idleMinutes) || 5,
      thresholdKB: Number(a.thresholdKB) || 32
    };
  });
  const [apps, setApps] = useState(initial);
  const [editing, setEditing] = useState(-1);
  const [filter, setFilter] = useState('');
  const [error, setError] = useState('');
  const runningNames = running.map(function (r) { return cleanName(r.name).toLowerCase(); });

  function patch(index, key, value) {
    setApps(apps.map(function (a, i) {
      if (i !== index) return a;
      const copy = Object.assign({}, a);
      copy[key] = value;
      return copy;
    }));
  }

  function removeApp(index) {
    setApps(apps.filter(function (a, i) { return i !== index; }));
    if (editing === index) setEditing(-1);
    if (editing > index) setEditing(editing - 1);
    setError('');
  }

  function addCard(name, processName) {
    const next = apps.concat([{
      name: name,
      enabled: true,
      processes: processName,
      transferProcesses: '',
      transferCommandLine: '',
      idleMinutes: 5,
      thresholdKB: 32
    }]);
    setApps(next);
    setEditing(next.length - 1);
    setFilter('');
    setError('');
  }

  function configured(processName) {
    const n = cleanName(processName).toLowerCase();
    return apps.some(function (a) {
      return splitNames(a.processes).some(function (p) { return cleanName(p).toLowerCase() === n; });
    });
  }

  function appRunning(app) {
    return splitNames(app.processes).some(function (p) {
      return runningNames.indexOf(cleanName(p).toLowerCase()) >= 0;
    });
  }

  function collect() {
    const out = [];
    const seen = {};
    for (let i = 0; i < apps.length; i++) {
      const a = apps[i];
      const display = String(a.name || '').trim();
      if (!display) { setError('第 ' + (i + 1) + ' 个软件还没有填写名称。'); setEditing(i); return null; }
      const key = display.toLowerCase();
      if (seen[key]) { setError('软件名称重复：' + display); setEditing(i); return null; }
      seen[key] = true;
      const procs = splitNames(a.processes).map(cleanName).filter(Boolean);
      if (procs.length === 0) { setError(display + ' 还没有填写监控进程。'); setEditing(i); return null; }
      for (let j = 0; j < procs.length; j++) {
        const p = procs[j];
        if (!/^[\p{L}\p{N}_.\- ]+$/u.test(p)) { setError('进程名不合法：' + p); setEditing(i); return null; }
        if (isReserved(p)) { setError('不能监控系统或外壳进程：' + p); setEditing(i); return null; }
      }
      const idle = Number(a.idleMinutes);
      if (!(idle >= 0.1 && idle <= 1440)) { setError(display + ' 的空闲分钟应为 0.1 到 1440。'); setEditing(i); return null; }
      const threshold = Number(a.thresholdKB);
      if (!(threshold >= 1 && threshold <= 1048576)) { setError(display + ' 的阈值应为 1 到 1048576 KB。'); setEditing(i); return null; }
      out.push({
        name: display,
        enabled: a.enabled !== false,
        processes: procs,
        transferProcesses: splitNames(a.transferProcesses).map(cleanName).filter(Boolean),
        transferCommandLine: String(a.transferCommandLine || '').trim(),
        idleMinutes: idle,
        thresholdKB: Math.round(threshold)
      });
    }
    if (out.length === 0) { setError('至少添加一个软件。'); return null; }
    return out;
  }

  function save() {
    setError('');
    const result = collect();
    if (!result) return;
    Quicker.close({ apps: result });
  }

  const q = filter.trim().toLowerCase();
  const visible = apps.map(function (a, i) { return { app: a, index: i }; }).filter(function (item) {
    if (!q) return true;
    const text = (item.app.name + ' ' + item.app.processes + ' ' + item.app.transferProcesses + ' ' + item.app.transferCommandLine).toLowerCase();
    return text.indexOf(q) >= 0;
  });
  const pickable = running.filter(function (r) {
    if (!r || !r.name) return false;
    if (isReserved(r.name) || configured(r.name)) return false;
    if (!q) return true;
    return (String(r.name) + ' ' + String(r.title || '')).toLowerCase().indexOf(q) >= 0;
  }).slice(0, 30);
  const enabledCount = apps.filter(function (a) { return a.enabled !== false; }).length;
  const runningCount = apps.filter(function (a) { return a.enabled !== false && appRunning(a); }).length;

  return (
    <main className='qk-ui-page'>
      <div className={dark ? 'app dark' : 'app'}>
        <style>{css}</style>
        <header className='head'>
          <div>
            <h1>传输空闲清理</h1>
            <p className='sub'>软件连续没有上传或下载时，自动结束后台进程。可添加任意软件，并单独设置进程与空闲规则。</p>
          </div>
          <div className={monitor.running ? 'pill pill-on' : 'pill'}>
            <span className={monitor.running ? 'dot dot-on' : 'dot'}></span>
            {monitor.running ? '监控运行中' : '监控未运行'}
          </div>
        </header>

        <div className='toolbar'>
          <div className='search'>
            <svg className='search-icon' viewBox='0 0 20 20' aria-hidden='true'>
              <circle cx='8.6' cy='8.6' r='5.4' fill='none' stroke='currentColor' strokeWidth='1.8' />
              <path d='M12.7 12.7 L16.6 16.6' fill='none' stroke='currentColor' strokeWidth='1.8' strokeLinecap='round' />
            </svg>
            <input value={filter} onChange={function (e) { setFilter(e.target.value); }} placeholder='搜索软件、进程或运行中的程序…' />
          </div>
          <button className='qk-ui-btn btn btn-add' onClick={function () { addCard('', ''); }}><span className='plus'>+</span>手动添加</button>
        </div>

        <div className='picker'>
          <div className='picker-label'><span className='live'></span>正在运行</div>
          <div className='chips'>
            {pickable.map(function (r, i) {
              return (
                <button key={i} className='qk-ui-btn chip' title={r.title || r.name} onClick={function () { addCard(cleanName(r.name), cleanName(r.name)); }}>
                  <span className='cplus'>+</span>{cleanName(r.name)}
                </button>
              );
            })}
            {pickable.length === 0 ? <span className='picker-empty'>没有可加入的运行中软件，试试上方搜索或手动添加。</span> : null}
          </div>
        </div>

        <div className='list-panel'>
          <div className='list-head'>
            <div className='list-title'>软件列表 <span className='count'>{apps.length}</span></div>
            <div className='list-meta'>{q ? '找到 ' + visible.length + ' 个匹配项' : enabledCount + ' 个启用 · ' + runningCount + ' 个运行中'}</div>
          </div>
          <div className='list-body'>
            {visible.map(function (item) {
              const a = item.app;
              const i = item.index;
              const on = appRunning(a);
              const enabled = a.enabled !== false;
              const isEditing = editing === i;
              return (
                <div className={isEditing ? 'row-wrap open' : 'row-wrap'} key={i}>
                  <div className='row'>
                    <div className={'avatar tint-' + tintIndex(a.name)}>
                      {initials(a.name)}
                      <span className={enabled && on ? 'badge live' : 'badge'}></span>
                    </div>
                    <div className='row-main' onClick={function () { setEditing(isEditing ? -1 : i); setError(''); }}>
                      <div className='row-title'>
                        <span className='name'>{a.name || '未命名软件'}</span>
                        <span className={!enabled ? 'state off' : (on ? 'state on' : 'state')}>{!enabled ? '已停用' : (on ? '运行中' : '未运行')}</span>
                      </div>
                      <div className='row-sub'>监控进程 · {procSummary(splitNames(a.processes).map(cleanName))}</div>
                      <div className='tags'>
                        <span className='tag tag-accent'>空闲 {formatNumber(a.idleMinutes)} 分钟</span>
                        <span className='tag'>传输 {transferSummary(a)}</span>
                        {String(a.transferCommandLine || '').trim() ? <span className='tag'>筛选 {brief(a.transferCommandLine, 18)}</span> : null}
                        <span className='tag tag-dim'>阈值 {formatNumber(a.thresholdKB)} KB</span>
                      </div>
                    </div>
                    <div className='row-actions'>
                      <label className='switch' title={enabled ? '已启用' : '已停用'}>
                        <input type='checkbox' checked={enabled} onChange={function (e) { patch(i, 'enabled', e.target.checked); }} />
                        <span className='slider'></span>
                      </label>
                      <div className='row-btns'>
                        <button className='qk-ui-btn btn btn-ghost' onClick={function () { setEditing(isEditing ? -1 : i); setError(''); }}>{isEditing ? '收起' : '编辑'}</button>
                        <button className='qk-ui-btn btn btn-ghost danger' onClick={function () { removeApp(i); }}>删除</button>
                      </div>
                    </div>
                  </div>
                  {isEditing ? (
                    <div className='editor'>
                      <div className='editor-box'>
                        <div className='editor-grid'>
                          <div className='field span2'>
                            <label>软件名称</label>
                            <input value={a.name} onChange={function (e) { patch(i, 'name', e.target.value); }} placeholder='例如：百度网盘' />
                          </div>
                          <div className='field'>
                            <label>空闲分钟</label>
                            <input type='number' min='0.1' max='1440' step='0.5' value={a.idleMinutes} onChange={function (e) { patch(i, 'idleMinutes', e.target.value); }} />
                          </div>
                          <div className='field'>
                            <label>采样阈值 KB</label>
                            <input type='number' min='1' max='1048576' step='1' value={a.thresholdKB} onChange={function (e) { patch(i, 'thresholdKB', e.target.value); }} />
                          </div>
                          <div className='field span2'>
                            <label>监控进程<span className='opt'>分号分隔 · 判断是否打开并结束后台</span></label>
                            <input value={a.processes} onChange={function (e) { patch(i, 'processes', e.target.value); }} placeholder='例如：BaiduNetdisk; baidunetdiskhost' />
                          </div>
                          <div className='field span2'>
                            <label>传输进程<span className='opt'>分号分隔 · 留空表示全部</span></label>
                            <input value={a.transferProcesses} onChange={function (e) { patch(i, 'transferProcesses', e.target.value); }} placeholder='留空即可；百度网盘填 baidunetdiskhost' />
                          </div>
                          <div className='field span2'>
                            <label>命令行筛选<span className='opt'>可选</span></label>
                            <input value={a.transferCommandLine} onChange={function (e) { patch(i, 'transferCommandLine', e.target.value); }} placeholder='只统计命令行包含此文字的传输进程' />
                          </div>
                          <div className='editor-note'>当前进程{on ? '正在运行' : '未运行'}。结束时会连同子进程一并关闭，请先保存手头工作。</div>
                        </div>
                      </div>
                    </div>
                  ) : null}
                </div>
              );
            })}
            {visible.length === 0 ? (
              <div className='empty'>
                <div className='empty-icon'>
                  <svg viewBox='0 0 24 24' width='22' height='22' fill='none' stroke='currentColor' strokeWidth='1.7' strokeLinecap='round' strokeLinejoin='round'>
                    <rect x='4' y='5' width='16' height='14' rx='3'></rect>
                    <path d='M8 10h8M8 14h5'></path>
                  </svg>
                </div>
                <div className='empty-title'>{q ? '没有匹配的软件' : '还没有添加软件'}</div>
                <div className='empty-sub'>{q ? '换个关键词，或清空搜索查看全部。' : '从上方运行中的程序点选，或手动添加。'}</div>
              </div>
            ) : null}
          </div>
          <div className='list-foot'>
            <span>结束时会关闭所选软件的后台进程与子进程</span>
            <span className='foot-right'>上传与下载都会顺延空闲时间</span>
          </div>
        </div>

        <footer className='foot'>
          <div className={error ? 'error' : 'hint'}>{error || (monitor.running ? '保存后立即生效，无需重启。' : '保存后会尝试启动后台监控。')}</div>
          <div className='foot-btns'>
            <button className='qk-ui-btn btn' onClick={function () { Quicker.cancel(); }}>取消</button>
            <button className='qk-ui-btn btn btn-primary' onClick={save}>保存并关闭</button>
          </div>
        </footer>
      </div>
    </main>
  );
}
