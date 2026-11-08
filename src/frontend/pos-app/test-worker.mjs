import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('./public/sw.js',import.meta.url),'utf8');
function worker(fetchImpl=async()=>({ok:true,clone(){return this;}})) {
  const handlers={},deleted=[],puts=[],waits=[];let claimed=false;
  vm.runInNewContext(source,{URL,Promise,fetch:fetchImpl,self:{location:{origin:'http://pos.test'},clients:{claim:async()=>{claimed=true;}},addEventListener:(name,cb)=>handlers[name]=cb},caches:{keys:async()=>['oisms-pos-v1.0','oisms-pos-v2.0','other-app'],delete:async key=>deleted.push(key),match:async()=>({offlineShell:true}),open:async()=>({addAll:async()=>{},put:async(key,value)=>puts.push({key,value})})}});
  return {handlers,deleted,puts,waits,claimed:()=>claimed,event(request){let response;return {request,waitUntil:p=>waits.push(p),respondWith:p=>response=p,response:()=>response};}};
}
test('worker: activate chỉ xóa cache POS cũ rồi claim',async()=>{const w=worker();w.handlers.activate({waitUntil:p=>w.waits.push(p)});await Promise.all(w.waits);assert.deepEqual(w.deleted,['oisms-pos-v1.0','oisms-pos-v2.0']);assert.ok(w.claimed());});
test('worker: install chỉ precache, không claim trước activate',async()=>{const w=worker();w.handlers.install({waitUntil:p=>w.waits.push(p)});await Promise.all(w.waits);assert.equal(w.claimed(),false);});
test('worker: không cache API/hub/checkout POST',()=>{for(const request of [{method:'POST',url:'http://pos.test/api/pos/checkout'},{method:'GET',url:'http://pos.test/api/products'},{method:'GET',url:'http://pos.test/hubs/notifications'}]){const w=worker(),e=w.event(request);w.handlers.fetch(e);assert.equal(e.response(),undefined);}});
test('worker: online cập nhật HTML cache, offline dùng shell mới nhất',async()=>{const w=worker(),e=w.event({method:'GET',mode:'navigate',url:'http://pos.test/'});w.handlers.fetch(e);await e.response();await Promise.all(w.waits);assert.equal(w.puts[0].key,'/index.html');const offline=worker(async()=>{throw Error('offline');}),off=offline.event({method:'GET',mode:'navigate',url:'http://pos.test/'});offline.handlers.fetch(off);assert.ok((await off.response()).offlineShell);});
