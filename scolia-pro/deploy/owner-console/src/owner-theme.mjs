// Charte professionnelle de la console propriétaire : CSS local bundle Wrangler.
// Aucun contenu externe, aucune police ou bibliothèque chargée sur un autre domaine.
export const ownerThemeCss = `
:root{color-scheme:light;--navy:#10223c;--navy2:#1b3c69;--blue:#246be5;--muted:#6e8098;--border:#e4eaf3;--canvas:#f4f7fc;--paper:#fff;--shadow:0 7px 25px rgba(18,44,78,.055)}
*{box-sizing:border-box}
html{scroll-behavior:smooth;scroll-padding-top:88px}
body{background:var(--canvas);color:var(--navy);letter-spacing:-.01em}
[hidden]{display:none!important}
header{position:relative;isolation:isolate;background:linear-gradient(116deg,#10223d 0%,#18365c 65%,#214a82 100%);color:#f7fbff;border-bottom:0;padding:27px max(20px,calc((100vw - 1160px)/2)) 35px;overflow:hidden}
header:after{content:"";position:absolute;z-index:-1;right:-140px;top:-180px;height:400px;width:400px;border-radius:50%;background:radial-gradient(circle,#4183ee44,transparent 71%)}
header small{color:#b9cde9;font-size:13px;line-height:1.5;display:block;max-width:680px}
header h1{font-size:clamp(25px,3.3vw,36px);letter-spacing:-.04em;margin:14px 0 7px;line-height:1.14;max-width:830px}
.owner-brandline{display:flex;align-items:center;gap:11px}
.owner-mark{display:grid;place-items:center;background:linear-gradient(145deg,#3984fd,#1760d9);border:1px solid #86b5ff65;border-radius:15px;height:46px;width:46px;box-shadow:0 8px 20px #050f2050;font-size:20px;font-weight:800;color:white;letter-spacing:-.08em}
.owner-brandline small{font-size:12px;font-weight:750;letter-spacing:.065em;color:#d4e4f7}
.owner-tag{font-size:11px!important;letter-spacing:.025em!important;color:#c4e7e5!important;border:1px solid #93d6d455;display:inline-flex!important;border-radius:100px;background:#ffffff10;padding:7px 11px;margin-left:auto}
.wrap{max-width:1200px;margin:0 auto;padding:24px 23px 55px}
.owner-navigation{position:sticky;top:0;z-index:5;display:flex;align-items:center;gap:5px;overflow-x:auto;white-space:nowrap;scrollbar-width:thin;margin:-24px -23px 25px;padding:13px 22px;background:#f8fafdf5;backdrop-filter:blur(18px);border-bottom:1px solid var(--border)}
.owner-navigation a{font-size:13px;font-weight:700;color:#4c6280;text-decoration:none;border-radius:11px;padding:10px 14px;transition:background .18s,color .18s}
.owner-navigation a:hover,.owner-navigation a:focus-visible{background:#e9f1ff;color:#125dd3}
.owner-navigation a:first-child{background:#e5efff;color:#1c63c9}
.cards{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:13px}
.card,.panel{background:var(--paper);border:1px solid var(--border);border-radius:20px;box-shadow:var(--shadow);padding:20px}
.card{min-height:127px;display:flex;flex-direction:column;justify-content:space-between;position:relative;overflow:hidden}
.card:before{position:absolute;content:"";width:8px;height:8px;border-radius:50%;right:20px;top:22px;background:#60a5fa;box-shadow:0 0 0 5px #e6f1ff}
.card:nth-child(3n+2):before{background:#22b6a6;box-shadow:0 0 0 5px #e4f9f2}
.card:nth-child(3n):before{background:#f0ac54;box-shadow:0 0 0 5px #fff6e9}
.card small{font-size:12px;letter-spacing:0;font-weight:650;color:var(--muted);padding-right:18px}
.metric{font-size:clamp(28px,3vw,37px);font-weight:800;letter-spacing:-.05em;margin:6px 0 0;color:var(--navy)}
.panel{margin-top:16px;padding:24px 25px;scroll-margin-top:82px}
.panel h2{font-size:20px;letter-spacing:-.03em;font-weight:760;margin:0 0 13px}
.panel p:not(.error){line-height:1.65}
#auth{max-width:610px;margin:42px auto;box-shadow:0 15px 60px #193b6313;padding:29px}
#auth h2{font-size:25px}
#auth .toolbar{margin-top:20px}
button{font:inherit;font-weight:680;background:var(--blue);border-radius:11px;min-height:39px;padding:10px 16px;transition:transform .1s,background .18s,box-shadow .18s;box-shadow:0 2px 5px #12355f17}
button:hover{background:#1258d4}
button:active{transform:translateY(1px)}
button.secondary{background:#eef4ff;color:#1f548f;box-shadow:none}
button.secondary:hover{background:#dfecff}
input{max-width:100%;min-width:0;min-height:44px;border:1px solid #d9e2ef;border-radius:12px;background:#fbfcff;box-shadow:0 1px 2px #10233b08;padding:10px 13px}
input:focus{outline:3px solid #b9d5ff73;outline-offset:1px;border-color:#6b9dec;background:#fff}
.toolbar{display:flex;gap:9px;align-items:center;flex-wrap:wrap;margin:15px 0}
.toolbar>input{flex:1 1 205px;width:auto}
.toolbar>label{flex:1 1 200px;color:#546c88;font-size:13px}
.toolbar>label>input{display:block;width:100%;margin-top:7px}
.toolbar button{flex:0 1 auto}
table{width:100%;min-width:550px;border-collapse:collapse}
thead{background:#f6f9fe}
th{font-size:11px;font-weight:780;text-transform:uppercase;letter-spacing:.055em;color:#697d97}
th,td{padding:13px 10px;line-height:1.4;border-bottom:1px solid #edf1f7;vertical-align:middle}
tbody tr:hover{background:#f9fbff}
.info{font-size:12.5px;color:#687e99}
.error{font-size:13px;color:#a92d4e;font-weight:650}
#details{white-space:pre-wrap;overflow:auto;background:#f6f9fe;padding:12px;border-radius:12px}
details{border:1px solid var(--border);border-radius:12px;padding:10px 14px}
details summary{cursor:pointer}
.owner-footer{text-align:center;color:#90a0b3;font-size:12px;margin-top:29px}
@media(max-width:840px){
 .cards{grid-template-columns:repeat(2,minmax(0,1fr))}
 .wrap{padding:19px 16px 48px}
 .owner-navigation{margin:-19px -16px 20px;padding:10px 12px}
 .card,.panel{padding:18px}
}
@media(max-width:540px){
 header{padding:19px 18px 26px}
 header h1{font-size:25px;margin:12px 0 8px}
 header small{font-size:12px}
 .owner-mark{height:40px;width:40px;border-radius:12px;font-size:18px}
 .owner-tag{font-size:10px!important;white-space:nowrap}
 .wrap{padding:12px 12px 45px}
 .owner-navigation{margin:-12px -12px 14px;padding:9px 10px}
 .owner-navigation a{font-size:12px;padding:9px 11px}
 .cards{gap:10px;grid-template-columns:repeat(2,minmax(0,1fr))}
 .card{padding:14px;min-height:106px;border-radius:16px}
 .card small{font-size:11px;line-height:1.3}
 .card:before{right:12px;top:13px;width:6px;height:6px;box-shadow:0 0 0 3px #e6f1ff}
 .metric{font-size:30px}
 .panel{margin-top:12px;padding:17px;border-radius:17px}
 .panel h2{font-size:17px;margin-bottom:11px}
 .toolbar{gap:9px}
 .toolbar button{flex:1 1 100%;text-align:center}
 .toolbar>input{flex:1 1 100%}
 #auth{margin:17px auto;padding:20px}
 #auth h2{font-size:23px}
 table{display:table;width:100%;min-width:500px}
}
@media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}button{transition:none}}
`;
