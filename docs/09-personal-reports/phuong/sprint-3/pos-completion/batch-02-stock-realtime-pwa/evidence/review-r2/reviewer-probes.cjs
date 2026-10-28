const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {world,branchA,branchB,userA,userB,sku,response,defer,find,input,flush}=require('./harness.cjs');
const observations=[];
(async()=>{
 // F01 original deferred-start/owner counterexample now closed independently.
 {
  const w=world(async()=>response([]));w.setHold(true);
  const a=w.realtime.startRealtimeConnection(userA,branchA);await flush();const old=w.connections[0];
  await w.realtime.stopRealtimeConnection();w.api.setAuthToken('fixture-B');const b=w.realtime.startRealtimeConnection(userB,branchB);await flush();
  assert.equal(w.connections.length,2);const current=w.connections[1];current.startGate.resolve();await b;old.startGate.resolve();await a;
  const received=[];w.realtime.subscribeOrderCreated(e=>received.push(e.orderNumber));old.handlers.OrderCreated({orderId:'old',orderNumber:'PRIVATE-A'});
  assert.equal(old.stopped,2);assert.deepEqual(received,[]);assert.equal(current.invocations[0][1],branchB);
  observations.push({id:'F01-original',status:'PASS',connections:w.connections.length,oldStopped:old.stopped,oldOwnerDelivered:received,newJoin:current.invocations});
 }
 // F02 actual ProductSearch now receives fresh 8 through one shared cache.
 {
  const w=world(async url=>response(String(url).includes('/barcode/')?{...sku,available:8,onHand:8}:[{...sku,available:8,onHand:8}]));
  w.api.setAuthoritativeStock(branchA,sku.variantId,10);await w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,skuCode:sku.sku}]);
  const added=[],m=w.mountSearch({branchId:branchA,onAddToCart:x=>added.push(x),searchInputRef:{current:null}});
  input(m).props.onChange({target:{value:'BAR-A'}});m.settle();await input(m).props.onKeyDown({key:'Enter',preventDefault(){}});m.settle();
  assert.equal(w.api.getCachedStock(branchA,sku.variantId).available,8);assert.equal(w.stock.getCachedStock(branchA,sku.variantId).available,8);assert.equal(added[0].available,8);
  observations.push({id:'F02-original',status:'PASS',apiCache:8,sharedCache:8,addedAvailable:added[0].available});
 }
 // F03 no exact match no longer adopts first unrelated SKU.
 {
  const w=world(async()=>response([{...sku,variantId:'other-id',sku:'SKU-A-OTHER',available:99}]));w.api.setAuthoritativeStock(branchA,sku.variantId,10);
  const result=await w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,skuCode:sku.sku}]);const state=w.api.getCachedStock(branchA,sku.variantId);
  assert.equal(result.success,false);assert.equal(state.available,10);assert.equal(state.isStale,true);observations.push({id:'F03-original',status:'PASS',result,state});
 }
 // F05 original branch-switch barcode is correctly discarded.
 {
  const gate=defer(),w=world(async()=>{await gate.promise;return response({...sku,available:27})}),added=[];
  const props={branchId:branchA,onAddToCart:x=>added.push(x),searchInputRef:{current:null}},m=w.mountSearch(props);
  input(m).props.onChange({target:{value:'BAR-A'}});m.settle();const pending=input(m).props.onKeyDown({key:'Enter',preventDefault(){}});await flush();props.branchId=branchB;m.settle();gate.resolve();await pending;m.settle();
  assert.deepEqual(added,[]);observations.push({id:'F05-branch',status:'PASS',addedAfterBranchSwitch:added});
 }
 // F04: real backend search contract is textual; StockChanged carries only UUID.
 {
  const requests=[];let available=10;
  const w=world(async raw=>{const u=new URL(raw);requests.push(u.pathname+u.search);const q=u.searchParams.get('query')||'';const hits=[sku].filter(s=>[s.sku,s.productName,s.barcode].some(v=>v.toLowerCase().includes(q.toLowerCase())));return response(hits.map(s=>({...s,available,onHand:available})))});
  w.api.setAuthoritativeStock(branchA,sku.variantId,10);await w.realtime.startRealtimeConnection(userA,branchA);available=8;
  w.connections[0].handlers.StockLevelChanged({branchId:branchA,skuId:sku.variantId,newAvailableStock:8,timestamp:'2026-10-28T06:00:00Z'});await flush();
  const state=w.api.getCachedStock(branchA,sku.variantId);assert.equal(state.available,10);assert.equal(state.isStale,true);
  observations.push({id:'F04-contract',status:'REPRODUCED',expected:8,actual:state.available,isStale:state.isStale,requests,note:'Fixture mirrors PosCatalogController search predicate; UUID is not a searchable field'});
 }
 // F04: older SKU query overwrites newer barcode refetch of same variant.
 {
  const gate=defer();const w=world(async raw=>{const u=new URL(raw);if(u.pathname.includes('/barcode/'))return response({...sku,available:8,onHand:8});await gate.promise;return response([{...sku,available:10}])});
  const older=w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,skuCode:sku.sku}]);await flush();
  await w.api.invalidateAndRefetchStock(branchA,[{skuId:sku.variantId,barcode:sku.barcode}]);assert.equal(w.api.getCachedStock(branchA,sku.variantId).available,8);
  gate.resolve();await older;const state=w.api.getCachedStock(branchA,sku.variantId);assert.equal(state.available,10);
  observations.push({id:'F04-alias-race',status:'REPRODUCED',afterNewerBarcode:8,afterOlderSkuResponse:state.available,note:'Sequence keys differ: SKU code versus UUID although same variant'});
 }
 // Reconnect no longer has any stock-store subscriber after proxy consolidation.
 {
  let gets=0,available=10;const w=world(async()=>{gets++;return response([{...sku,available}])});w.api.setAuthoritativeStock(branchA,sku.variantId,10);
  await w.realtime.startRealtimeConnection(userA,branchA);available=8;await w.connections[0].reconnected('new-id');await flush();
  const state=w.api.getCachedStock(branchA,sku.variantId);assert.equal(gets,0);assert.equal(state.available,10);
  observations.push({id:'F04-reconnect',status:'REPRODUCED',getsAfterReconnect:gets,availableAfterReconnect:state.available,serverAvailable:available,joins:w.connections[0].invocations});
 }
 // F05 remaining query race: clear text while a search is in flight.
 {
  const gate=defer(),w=world(async()=>{await gate.promise;return response([sku])}),m=w.mountSearch({branchId:branchA,onAddToCart(){},searchInputRef:{current:null}});
  input(m).props.onChange({target:{value:'SKU-A'}});m.settle();w.advance(200);await flush();input(m).props.onChange({target:{value:''}});m.settle();
  gate.resolve();await flush();m.settle();const buttons=[];function visit(n){if(Array.isArray(n))return n.forEach(visit);if(!n||typeof n!=='object')return;if(typeof n.props?.onClick==='function'&&String(n.props.onClick).includes('handleSelectProduct'))buttons.push(n);visit(n.props?.children)}visit(m.tree());
  assert.equal(input(m).props.value,'');assert.ok(buttons.length>0);
  observations.push({id:'F05-query-clear',status:'REPRODUCED',inputValue:input(m).props.value,oldResultsReopened:buttons.length});
 }
 fs.writeFileSync(path.join(__dirname,'probe-results.json'),JSON.stringify({provider:'Actual TS/TSX in VM using R1 controlled hooks/storage/fetch/fake transport. Search fixture mirrors verified backend predicate; no live backend/DB.',observations},null,2));console.log(JSON.stringify(observations,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
