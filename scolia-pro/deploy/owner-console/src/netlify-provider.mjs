// SCOLIA Console : troisième fournisseur HTTPS (pilote désactivé).
// Netlify n'est PAS un serveur WebSocket ni TURN.
export const NETLIFY_BACKUP_SERVICE = "SCOLIA Netlify HTTPS Backup";
const SITE=/^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?\.netlify\.app$/i;
export function normalizeNetlifyUrl(input) {
  if(typeof input!=="string"||input.length>210)return null;
  try {
    const uri=new URL(input.trim());
    if(uri.protocol!=="https:"||!SITE.test(uri.hostname)||uri.username||uri.password||
      uri.search||uri.hash||uri.port||uri.pathname!=="/")return null;
    return "https://"+uri.hostname.toLowerCase();
  } catch { return null; }
}
function normalizedLabel(value) {
  if(typeof value!=="string")return "Netlify — secours HTTPS";
  const name=value.replace(/[\u0000-\u001f\u007f]/g," ").replace(/\s+/g," ").trim().slice(0,90);
  return name.length>=3?name:"Netlify — secours HTTPS";
}
function status(cfg) {
  return {provider:"netlify",name:cfg?.label||"Netlify — secours HTTPS",
    base_url:cfg?.base_url||null,protocol:"https-mailbox-v1",
    purpose:"secours HTTPS expérimental ; NON compatible WSS/TURN",
    configured:Boolean(cfg?.base_url),state:"disabled",can_activate:false,
    school_clients_connected:0,
    notice:"Le canal Netlify n'est pas raccordé aux clients scolaires. Aucun basculement automatique n'est possible avant validation du transport et accord des établissements."};
}
export async function netlifyProviderRequest(request,env,url,{jsonBody,fail,result,audit,nowMs}) {
  const db=env.OWNER_DB;
  const existing=await db.prepare("SELECT label,base_url FROM owner_provider_netlify WHERE id=1").first();
  if(request.method==="GET"&&url.pathname==="/api/v1/owner/relays/netlify")
    return result(status(existing));
  if(request.method==="PUT"&&url.pathname==="/api/v1/owner/relays/netlify"){
    const body=await jsonBody(request);
    if(!body||body.enabled!==false||body.confirm_experimental!==true)
      return fail("Confirmer que Netlify reste désactivé et expérimental.",400);
    const base=normalizeNetlifyUrl(body.base_url);
    if(!base)return fail("URL https://NOM.netlify.app valide requise, sans port ni chemin.",400);
    const label=normalizedLabel(body.label),now=nowMs();
    const sql="INSERT INTO owner_provider_netlify(id,label,base_url,state,updated_at)"+
      " VALUES(1,?,?,'disabled',?) ON CONFLICT(id) DO UPDATE SET "+
      "label=excluded.label,base_url=excluded.base_url,state='disabled',updated_at=excluded.updated_at";
    await db.prepare(sql).bind(label,base,now).run();
    await audit(db,"netlify_backup_configured_disabled",null,now);
    return result(status({label,base_url:base}));
  }
  if(request.method==="DELETE"&&url.pathname==="/api/v1/owner/relays/netlify"){
    await db.prepare("DELETE FROM owner_provider_netlify WHERE id=1").run();
    await audit(db,"netlify_backup_removed",null,nowMs());
    return result(status(null));
  }
  if(request.method==="POST"&&url.pathname==="/api/v1/owner/relays/netlify/test"){
    if(!existing?.base_url)return fail("Configurer d'abord le site Netlify.",409);
    const endpoint=normalizeNetlifyUrl(existing.base_url);
    if(!endpoint)return fail("URL Netlify enregistrée invalide.",409);
    const fetcher=env.NETLIFY_HEALTH_FETCH||fetch;
    try{
      const response=await fetcher(endpoint+"/api/scolia-backup/health",{
        method:"GET",headers:{"Accept":"application/json"},
        redirect:"manual",signal:AbortSignal.timeout(6000)
      });
      if(response.status!==200)return result({provider:"netlify",reachable:false,
        http_status:response.status,can_activate:false,
        notice:"Point de santé indisponible ou redirigé ; aucun relais activé."});
      const data=await response.json();
      if(data?.service!==NETLIFY_BACKUP_SERVICE||
         data?.protocol!=="scolia-https-mailbox-v1"||data?.transport!=="https")
        return result({provider:"netlify",reachable:false,can_activate:false,
          notice:"Le site répond mais n'expose pas le protocole SCOLIA prévu."});
      return result({provider:"netlify",reachable:true,http_status:200,
        mailbox_enabled:data.enabled===true,can_activate:false,
        notice:"Netlify répond via HTTPS ; les applications SCOLIA ne sont pas encore reliées à ce canal."});
    }catch{
      return result({provider:"netlify",reachable:false,can_activate:false,
        notice:"Netlify momentanément inaccessible. Les relais WSS existants restent inchangés."});
    }
  }
  return fail("Commande Netlify inconnue.",404);
}
