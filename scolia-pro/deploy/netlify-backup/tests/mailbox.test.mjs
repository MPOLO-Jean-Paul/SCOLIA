import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { handleMailbox,PROTOCOL } from "../src/mailbox.mjs";

const host="a".repeat(64),client="b".repeat(64),wrong="c".repeat(64);
const hash=x=>createHash("sha256").update(x).digest("hex");
const room=hash(host),epoch=Buffer.alloc(32,5).toString("base64"),
  frame=Buffer.alloc(48,7).toString("base64"),id="1".repeat(32);
const prefix="https://pilot.netlify.app/api/scolia-backup";
class Store{
  objects=new Map();serial=0;
  async get(k){return structuredClone(this.objects.get(k)?.value??null);}
  async getWithMetadata(k){
    const x=this.objects.get(k);return x?{data:structuredClone(x.value),etag:x.etag}:null;
  }
  async setJSON(k,v,o={}){
    const old=this.objects.get(k);
    if(o.onlyIfNew&&old || o.onlyIfMatch&&old?.etag!==o.onlyIfMatch)return{modified:false};
    const etag=String(++this.serial);
    this.objects.set(k,{value:structuredClone(v),etag});return{modified:true,etag};
  }
  async list({prefix}){return{blobs:[...this.objects.keys()]
    .filter(k=>k.startsWith(prefix)).map(key=>({key}))};}
  async delete(k){this.objects.delete(k);}
}
function req(path,method="GET",body=null,role=null,key=""){
  const h=role?{["X-Scolia-"+(role==="host"?"Host":"Client")]:key}:{};
  return new Request(prefix+path,{method,headers:h,
    body:body?JSON.stringify(body):undefined});
}
async function session(){
  const store=new Store();let clock=1000;
  const invoke=async (r,enabled=true)=>{
    const res=await handleMailbox(r,{store,enabled,now:()=>clock});
    return{code:res.status,data:await res.json()};
  };
  const created=await invoke(req("/session?room="+room,"POST",
    {client_hash:hash(client),epoch},"host",host));
  assert.equal(created.code,200);
  return{store,invoke,advance:ms=>clock+=ms};
}
test("Health works while experimental relay is off by default",async()=>{
  const store=new Store();
  const res=await handleMailbox(req("/health"),{store,enabled:false});
  const data=await res.json();
  assert.equal(data.protocol,PROTOCOL);
  assert.equal(data.enabled,false);assert.equal(data.websocket,false);
  assert.equal(data.production_ready,false);
  const off=await handleMailbox(req("/session?room="+room,"POST",
    {client_hash:hash(client),epoch},"host",host),{store,enabled:false});
  assert.equal(off.status,503);assert.equal(store.objects.size,0);
});
test("Wrong host proof cannot create a room",async()=>{
  const store=new Store();
  const res=await handleMailbox(req("/session?room="+room,"POST",
    {client_hash:hash(client),epoch},"host",wrong),{store,enabled:true});
  assert.equal(res.status,401);assert.equal(store.objects.size,0);
});
test("Authenticated bidirectional ciphertext, ack and no replay",async()=>{
  const{invoke}=await session();
  const accepted=await invoke(req("/mail?room="+room+"&role=client","POST",
    {id,ciphertext:frame},"client",client));
  assert.equal(accepted.code,201);
  let got=await invoke(req("/mail?room="+room+"&role=host","GET",null,"host",host));
  assert.equal(got.data.messages[0].ciphertext,frame);
  assert.equal((await invoke(req("/mail?room="+room+"&role=host&id="+id,
    "DELETE",null,"host",host))).code,200);
  got=await invoke(req("/mail?room="+room+"&role=host","GET",null,"host",host));
  assert.equal(got.data.messages.length,0);
  assert.equal((await invoke(req("/mail?room="+room+"&role=client","POST",
    {id,ciphertext:frame},"client",client))).code,409);
  assert.equal((await invoke(req("/mail?room="+room+"&role=host","POST",
    {id:"2".repeat(32),ciphertext:frame},"host",host))).code,201);
  got=await invoke(req("/mail?room="+room+"&role=client","GET",null,"client",client));
  assert.equal(got.data.messages.length,1);
});
test("Role spoofing, web origin and oversized frame refused",async()=>{
  const{invoke}=await session();
  assert.equal((await invoke(req("/mail?room="+room+"&role=host",
    "GET",null,"client",client))).code,401);
  assert.equal((await invoke(req("/mail?room="+room+"&role=client","POST",
    {id,ciphertext:Buffer.alloc(9000).toString("base64")},"client",client))).code,400);
  const web=req("/mail?room="+room+"&role=client","GET",null,"client",client);
  web.headers.set("Origin","https://example.org");
  assert.equal((await invoke(web)).code,403);
});
test("Expired sessions prevent delivery",async()=>{
  const{invoke,advance}=await session();
  assert.equal((await invoke(req("/mail?room="+room+"&role=client","POST",
    {id,ciphertext:frame},"client",client))).code,201);
  advance(91000);
  assert.equal((await invoke(req("/mail?room="+room+"&role=host",
    "GET",null,"host",host))).code,410);
});
test("Back-pressure after 24 opaque messages",async()=>{
  const{invoke}=await session();
  for(let i=0;i<24;i++){
    const res=await invoke(req("/mail?room="+room+"&role=client","POST",
      {id:i.toString(16).padStart(32,"0"),ciphertext:frame},"client",client));
    assert.equal(res.code,201);
  }
  assert.equal((await invoke(req("/mail?room="+room+"&role=client","POST",
    {id:"f".repeat(32),ciphertext:frame},"client",client))).code,429);
});
