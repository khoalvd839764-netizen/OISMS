const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {world,branchA,userA,sku,response,defer,input,flush}=require('./harness.cjs');
const observations=[];
(async()=>{
 // Correct controller DTO for an ID-only event, no text-match shortcut in fixture.
 {
  let available=10;const requests=[];
  const w=world(async raw=>{const u=new URL(raw);requests.push(u.pathname+u.search);if(u.pathname.startsWith('/api/inventory/balances/sku/'))return response({skuId:sku.variantId,skuCode:sku.sku,available,onHand:available,reserved:0});const q=u.searchParams.get('query')||'';return response([sku].filter(s=>[s.sku,s.productName,s.barcode].some(v=>v.toLowerCase().includes(q.toLowerCase()))).map(s=>({...s,available})))});
  w.api.setAuthoritativeStock(branchA,sku.variantId,10);await w.realtime.startRealtimeConnection(userA,branchA);available=8;
  w.connections[0].handlers.StockLevelChanged({branchId:branchA,skuId:sku.variantId,newAvailableStock:8,timestamp:'2026-10-28T06:00:00Z'});await flush();
  const state=w.api.getCachedStock(branchA,sku.variantId);assert.equal(state.available,8);assert.equal(state.isStale,false);assert.ok(requests.some(x=>x.includes('/inventory/balances/sku/')));
  observations.push({case:'F04-ID-contract',status:'PASS',state,requests});
 }
 // R2's canonical ID helper-to-helper case now passes.
 {
  const gate=defer(),w=world(async raw=>String(raw).includes('/barcode/')?response({...sku,available:8}):(await gate.promise,response([{...sku,available:10}])));
  const older=w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,skuCode:sku.sku}]);await flush();await w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,barcode:sku.barcode}]);gate.resolve();await older;
  assert.equal(w.api.getCachedStock(branchA,sku.variantId).available,8);observations.push({case:'F04-R2-alias-helper',status:'PASS',available:8});
 }
 // Reconnect uses real balance shape and performs GET.
 {
  let gets=0,available=10;const w=world(async raw=>{gets++;return response(String(raw).includes('/inventory/balances/')?{skuId:sku.variantId,skuCode:sku.sku,available,onHand:available,reserved:0}:[{...sku,available}])});
  w.api.setAuthoritativeStock(branchA,sku.variantId,10);await w.realtime.startRealtimeConnection(userA,branchA);available=8;await w.connections[0].reconnected('new-id');await flush();
  assert.ok(gets>0);assert.equal(w.api.getCachedStock(branchA,sku.variantId).available,8);observations.push({case:'F04-reconnect',status:'PASS',gets,available:8});
 }
 // Query clear counterexample now stays closed.
 {
  const gate=defer(),w=world(async()=>{await gate.promise;return response([sku])}),m=w.mountSearch({branchId:branchA,onAddToCart(){},searchInputRef:{current:null}});
  input(m).props.onChange({target:{value:'SKU-A'}});m.settle();w.advance(200);await flush();input(m).props.onChange({target:{value:''}});m.settle();gate.resolve();await flush();m.settle();
  const rows=[];function visit(n){if(Array.isArray(n))return n.forEach(visit);if(!n||typeof n!=='object')return;if(typeof n.props?.onClick==='function'&&String(n.props.onClick).includes('handleSelectProduct'))rows.push(n);visit(n.props?.children)}visit(m.tree());assert.equal(input(m).props.value,'');assert.equal(rows.length,0);observations.push({case:'F05-query-clear',status:'PASS',input:'',oldRows:rows.length});
 }
 // Production App passes receipt SKU code as both skuId and skuCode (App.tsx 699-702).
 {
  const gate=defer(),w=world(async raw=>String(raw).includes('/barcode/')?response({...sku,available:8}):(await gate.promise,response([{...sku,available:10}])));
  w.api.setAuthoritativeStock(branchA,sku.variantId,10,{skuId:sku.variantId,skuCode:sku.sku,barcode:sku.barcode});
  const older=w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.sku,skuCode:sku.sku}]);await flush();await w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,barcode:sku.barcode}]);const before=w.api.getCachedStock(branchA,sku.variantId).available;gate.resolve();await older;const after=w.api.getCachedStock(branchA,sku.variantId).available;
  assert.equal(before,8);assert.equal(after,8);observations.push({case:'F04-App-code-as-ID',status:'PASS',checkoutArgs:{skuId:sku.sku,skuCode:sku.sku},afterNewer:before,afterOlder:after});
 }
 // ProductSearch writes barcode directly, outside invalidateAndRefetchStock sequence guard.
 {
  const gate=defer(),w=world(async raw=>String(raw).includes('/barcode/')?response({...sku,available:8}):(await gate.promise,response([{...sku,available:10}])));
  w.api.setAuthoritativeStock(branchA,sku.variantId,10,{skuId:sku.variantId,skuCode:sku.sku,barcode:sku.barcode});const older=w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,skuCode:sku.sku}]);await flush();
  const added=[],m=w.mountSearch({branchId:branchA,onAddToCart:x=>added.push(x),searchInputRef:{current:null}});input(m).props.onChange({target:{value:'BAR-A'}});m.settle();await input(m).props.onKeyDown({key:'Enter',preventDefault(){}});m.settle();const before=w.api.getCachedStock(branchA,sku.variantId).available;gate.resolve();await older;
  const after=w.api.getCachedStock(branchA,sku.variantId).available;assert.equal(added[0].available,8);assert.equal(before,8);assert.equal(after,8);observations.push({case:'F04-actual-barcode-versus-refetch',status:'PASS',addedAvailable:added[0].available,afterBarcode:before,afterOlderGET:after});
 }
 // Older barcode request completes AFTER a newer GET: completion-time bump treats stale data as fresh.
 {
  const gate=defer(),w=world(async raw=>{if(String(raw).includes('/barcode/')){await gate.promise;return response({...sku,available:10})}return response([{...sku,available:8}])});
  w.api.setAuthoritativeStock(branchA,sku.variantId,10,{skuId:sku.variantId,skuCode:sku.sku,barcode:sku.barcode});
  const added=[],m=w.mountSearch({branchId:branchA,onAddToCart:x=>added.push(x),searchInputRef:{current:null}});input(m).props.onChange({target:{value:'BAR-A'}});m.settle();const barcode=input(m).props.onKeyDown({key:'Enter',preventDefault(){}});await flush();
  await w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,skuCode:sku.sku}]);const afterNewer=w.api.getCachedStock(branchA,sku.variantId).available;assert.equal(afterNewer,8);gate.resolve();await barcode;m.settle();const final=w.api.getCachedStock(branchA,sku.variantId).available;
  assert.equal(final,10);assert.equal(added[0].available,10);observations.push({case:'F04-older-barcode-after-newer-GET',status:'REPRODUCED',afterNewerGET:afterNewer,afterOlderBarcode:final,addedAvailable:added[0].available});
 }
 // Search now skips every fresh API response whenever any cache entry already exists.
 {
  const w=world(async()=>response([{...sku,available:8}]));w.api.setAuthoritativeStock(branchA,sku.variantId,10,{skuId:sku.variantId,skuCode:sku.sku,barcode:sku.barcode});const added=[],m=w.mountSearch({branchId:branchA,onAddToCart:x=>added.push(x),searchInputRef:{current:null}});
  input(m).props.onChange({target:{value:'SKU-A'}});m.settle();w.advance(200);await flush();m.settle();
  const rows=[];function visit(n){if(Array.isArray(n))return n.forEach(visit);if(!n||typeof n!=='object')return;if(typeof n.props?.onClick==='function'&&String(n.props.onClick).includes('handleSelectProduct'))rows.push(n);visit(n.props?.children)}visit(m.tree());assert.ok(rows.length);rows[0].props.onClick();
  const final=w.api.getCachedStock(branchA,sku.variantId).available;assert.equal(final,10);assert.equal(added[0].available,10);observations.push({case:'F04-search-fresh8-cached10',status:'REPRODUCED',responseAvailable:8,cacheAvailable:final,addedAvailable:added[0].available});
 }
 fs.writeFileSync(path.join(__dirname,'probe-results.json'),JSON.stringify({provider:'Actual TS/TSX and production callbacks/components in VM, controlled HTTP/storage/hooks/fake SignalR; balance fixture matches backend DTO. No backend/DB writes.',observations},null,2));console.log(JSON.stringify(observations,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});

