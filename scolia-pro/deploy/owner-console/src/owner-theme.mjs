// Surfaces et hiérarchie Fluent communes à SCOLIA Console native et web.
// Police système et illustrations embarquées : aucun chargement sur un autre domaine.
export const ownerThemeCss = `
:root{color-scheme:light dark;--canvas:#f4f6fa;--paper:#fff;--ink:#1d2735;--muted:#536278;--border:#dfe5ee;--blue:#005fb8;--onblue:#fff;--soft:#e5f0ff;--hover:#edf2f9;--fieldline:#8793a4;--error:#b42335;--radius:12px}
*{box-sizing:border-box;min-width:0}
html{scroll-behavior:smooth;scroll-padding-top:88px}
body{background:var(--canvas);color:var(--ink);font-family:"Segoe UI Variable","Segoe UI",system-ui,sans-serif;font-size:14px;line-height:1.5;letter-spacing:0}
[hidden]{display:none!important}
header{background:var(--canvas);color:var(--ink);border:0;padding:25px max(24px,calc((100vw - 1160px)/2)) 21px;overflow-wrap:anywhere}
header h1{font-size:28px;font-weight:600;letter-spacing:-.025em;line-height:1.25;margin:18px 0 8px}
header small{color:var(--muted);font-size:13px}
.owner-brandline{display:flex;gap:12px;align-items:center;flex-wrap:wrap}
.owner-mark{display:grid;place-items:center;height:42px;width:42px;border:1px solid var(--border);border-radius:11px;background:var(--paper);font-size:22px;font-weight:700;color:var(--blue)}
.owner-tag{margin-left:auto;display:inline-flex;align-items:center;gap:6px;background:var(--soft);color:var(--blue);border-radius:8px;padding:5px 9px;font-size:12px;letter-spacing:0}
.owner-tag svg{width:18px;height:18px}
.wrap{max-width:1208px;margin:0 auto;padding:0 24px 44px}
.owner-navigation{position:sticky;top:0;z-index:5;display:flex;gap:5px;flex-wrap:wrap;margin:0 0 20px;padding:10px 0;background:var(--canvas);border-bottom:1px solid var(--border)}
.owner-navigation a{display:inline-flex;align-items:center;gap:8px;color:var(--muted);text-decoration:none;white-space:normal;font-size:13px;font-weight:600;padding:10px 12px;border-radius:8px;min-height:44px}
.owner-navigation svg{width:22px;height:22px;flex-shrink:0}
.owner-navigation a:hover,.owner-navigation a:focus-visible,.owner-navigation a[aria-current]{color:var(--blue);background:var(--soft)}
.cards{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
.card,.panel{background:var(--paper);color:var(--ink);border:1px solid var(--border);border-radius:var(--radius);box-shadow:none;padding:20px}
.card{min-height:136px;position:relative;display:flex;flex-direction:column;justify-content:space-between;gap:8px}
.card small{font-size:12px;line-height:1.5;color:var(--muted)}
.card>small{overflow-wrap:anywhere}
.card:has(>.owner-metric-icon)>small{padding-right:36px}
.owner-metric-icon{position:absolute;right:16px;top:16px;width:32px;height:32px;border-radius:8px;background:var(--canvas);display:grid;place-items:center}
.owner-metric-icon svg{width:24px;height:24px}
.metric{font-size:26px;font-weight:600;letter-spacing:-.025em;margin:0;line-height:1.25;overflow-wrap:anywhere}
.panel{margin-top:16px;padding:22px;scroll-margin-top:90px}
h2,h3{font-weight:600;line-height:1.35;overflow-wrap:anywhere}
.panel h2{font-size:20px;margin:0 0 12px;display:flex;align-items:center;gap:10px}
.panel h2 svg{flex-shrink:0;width:26px;height:26px}
h3{font-size:16px;margin:24px 0 10px}
p{overflow-wrap:anywhere}
#auth{max-width:560px;margin:24px auto;padding:26px}
#auth h2{font-size:24px}
button{font:inherit;font-weight:600;color:var(--onblue);background:var(--blue);border:1px solid transparent;border-radius:8px;min-height:40px;max-width:100%;padding:9px 14px;line-height:1.45;white-space:normal;overflow-wrap:anywhere;box-shadow:none;transition:filter .12s,transform .12s}
button:hover{filter:brightness(.95)}
button:active{transform:translateY(1px)}
button.secondary{background:var(--soft);color:var(--blue);border-color:var(--border)}
button:disabled{cursor:wait;opacity:.6}
button:focus-visible,a:focus-visible,summary:focus-visible{outline:2px solid var(--blue);outline-offset:3px}
input{font:inherit;width:100%;max-width:100%;min-width:0;min-height:44px;padding:10px 12px;color:var(--ink);background:var(--paper);border:1px solid var(--fieldline);border-radius:8px;box-shadow:none}
input:focus{outline:2px solid var(--blue);outline-offset:1px}
input::placeholder{color:var(--muted);opacity:1}
.toolbar{display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap;margin:16px 0}
.toolbar>input{flex:1 1 220px;width:auto}
.toolbar>label{flex:1 1 220px;color:var(--muted);font-size:13px;font-weight:500;display:flex;flex-direction:column;gap:6px}
.toolbar>label>input{display:block;max-width:none}
.toolbar button{flex:0 1 auto}
.info{font-size:13px;color:var(--muted);line-height:1.6}
.error{font-size:14px;color:var(--error);font-weight:500}
#workspaceStatus:not(:empty){padding:12px 16px;margin:0 0 16px;background:var(--soft);border-radius:10px;color:var(--ink)}
#workspaceStatus.error{background:var(--paper);color:var(--error);border:1px solid var(--error)}
table{display:table;width:100%;min-width:0;border-collapse:collapse;table-layout:fixed}
th{font-size:12px;font-weight:600;letter-spacing:0;text-transform:none;background:var(--canvas);color:var(--muted)}
th,td{padding:12px 10px;line-height:1.5;border-bottom:1px solid var(--border);vertical-align:middle;overflow-wrap:anywhere;text-align:left}
tbody tr:hover{background:var(--hover)}
td button{font-size:13px}
details{border:1px solid var(--border);border-radius:10px;padding:12px 14px;margin:14px 0}
details summary{cursor:pointer;color:var(--muted)}
#details{white-space:pre-wrap;overflow-wrap:anywhere;max-height:420px;overflow:auto;background:var(--canvas);color:var(--ink);padding:14px;border-radius:8px}
.owner-footer{text-align:center;color:var(--muted);font-size:12px;margin-top:28px}
@media(max-width:900px){.cards{grid-template-columns:repeat(2,minmax(0,1fr))}.owner-navigation{position:relative}.panel{scroll-margin-top:16px}}
@media(max-width:600px){
 :root{--radius:16px}
 body{font-size:15px}
 header{padding:18px 16px}
 header h1{font-size:24px;margin-top:16px}
 .owner-brandline{gap:9px}
 .owner-brandline>small{font-size:12px;max-width:calc(100% - 55px)}
 .owner-tag{margin-left:0;font-size:12px}
 .wrap{padding:0 16px 32px}
 .owner-navigation{gap:6px;padding:4px 0 16px;margin-bottom:18px;display:grid;grid-template-columns:repeat(2,minmax(0,1fr))}
 .owner-navigation a{font-size:13px;background:var(--paper);border:1px solid var(--border);padding:10px;border-radius:10px}
 .cards{gap:10px}
 .card{padding:14px;min-height:140px}
 .owner-metric-icon{right:12px;top:12px;width:28px;height:28px}
 .owner-metric-icon svg{width:22px;height:22px}
 .metric{font-size:23px}
 .card small{font-size:13px}
 .panel{padding:16px;margin-top:14px}
 .panel h2{font-size:18px;align-items:flex-start}
 .toolbar{gap:12px}
 .toolbar>input,.toolbar>label{flex:1 1 100%}
 .toolbar button{flex:1 1 100%;min-height:48px}
 button,input{min-height:48px;border-radius:10px}
 #auth{margin:0 auto 20px;padding:20px}
 table,tbody,tr,td{display:block;width:100%;min-width:0}
 table{overflow:visible}
 thead{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)}
 tbody{display:grid;gap:12px}
 tbody tr{border:1px solid var(--border);border-radius:12px;padding:12px;background:var(--paper)}
 td{border:0;padding:6px 0;line-height:1.5;text-align:left}
 td[data-label]:before{content:attr(data-label);display:block;font-size:12px;font-weight:500;color:var(--muted);margin-bottom:3px}
 td[colspan]:before{content:none}
 td:last-child button{width:100%;min-height:44px}
}
@media(max-width:350px){.cards{grid-template-columns:1fr}.owner-brandline>small{max-width:calc(100% - 55px)}}
@media(prefers-color-scheme:dark){:root{--canvas:#10141c;--paper:#1a202b;--ink:#eef3fa;--muted:#aebbcd;--border:#303a4a;--blue:#9ecaff;--onblue:#062440;--soft:#173a58;--hover:#242c39;--fieldline:#8d99ac;--error:#ffacb6}}
@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}button{transition:none}}
`;
