const fs=require('node:fs');const path=require('node:path');
const harness=fs.readFileSync(path.join(__dirname,'harness.cjs'),'utf8');
const scenarios=String.raw`
(async()=>{
 const serverTime='2026-10-27T07:00:00Z';
 const expectedTime=new Date(serverTime).toLocaleString('vi-VN');
 const validReceipt={orderId:'55555555-5555-4555-8555-555555555555',orderNumber:'SERVER-1',receiptNumber:'SERVER-1',branchId:branchA,branchName:'A',branchAddress:null,branchPhone:null,cashierName:'A',createdAt:serverTime,paymentMethod:'Cash',totalAmount:100,amountGiven:100,changeDue:0,items:[{skuId:props.items[0].variantId,skuCode:'SKU-A',productName:'Server renamed product',quantity:1,unitPrice:100,subtotal:100}]};
 for(const kind of ['created-201','replay-200','empty-item','bad-metadata','bad-date','missing-date','null-date','empty-date','whitespace-date','number-date','mapper-throws']){
  let printed=[];const calls=[];let clock='2099-01-01T00:00:00Z';
  let receipt={...validReceipt};
  if(kind==='empty-item')receipt={...receipt,items:[{}]};
  if(kind==='bad-metadata')receipt.branchAddress=42;
  if(kind==='bad-date')receipt.createdAt='invalid-date';
  if(kind==='missing-date')delete receipt.createdAt;
  if(kind==='null-date')receipt.createdAt=null;
  if(kind==='empty-date')receipt.createdAt='';
  if(kind==='whitespace-date')receipt.createdAt='   ';
  if(kind==='number-date')receipt.createdAt=12345;
  const env=environment(async(url,init)=>{
    calls.push(JSON.parse(init.body));
    if(kind==='replay-200'&&calls.length===1)throw new TypeError('Injected response loss');
    return response(kind==='created-201'?201:200,{orderNumber:'SERVER-1',receipt});
  });
  env.Date=class FixedClock extends Date {constructor(...args){super(...(args.length?args:[clock]));}static now(){return Date.parse(clock);}};
  const api=apiFor(env);
  const usedApi=kind==='mapper-throws'?{...api,mapServerReceiptToReceiptData(){throw new Error('Injected mapping failure after API validation')}}:api;
  const m=modal(env,usedApi,{...props,currentUser:userA,onSuccessAndPrint:r=>printed.push(r)});
  await checkout(m);
  const pendingAfterFirst=api.getPendingCheckout(userA,branchA);
  const positive=kind==='created-201'||kind==='replay-200';
  if(kind==='replay-200'){assert.ok(pendingAfterFirst);clock='2100-01-01T00:00:00Z';await checkout(m);assert.equal(JSON.stringify(calls[0]),JSON.stringify(calls[1]));}
  if(positive){assert.equal(printed.length,1);assert.equal(printed[0].createdAt,expectedTime);assert.equal(printed[0].items[0].name,'Server renamed product');assert.equal(api.getPendingCheckout(userA,branchA),null);}
  else {assert.equal(printed.length,0);assert.ok(pendingAfterFirst);assert.equal(pendingAfterFirst.status,'unknown');clock='2100-01-01T00:00:00Z';await checkout(m);assert.equal(calls[0].requestKey,calls[1].requestKey);assert.equal(JSON.stringify(calls[0]),JSON.stringify(calls[1]));assert.ok(api.getPendingCheckout(userA,branchA));}
  observations.push({kind,printedCount:printed.length,pendingKeptAfterFirst:Boolean(pendingAfterFirst),pendingKeptAfterRetry:Boolean(api.getPendingCheckout(userA,branchA)),sourceCreatedAt:receipt.createdAt??null,mappedCreatedAt:printed[0]?.createdAt??null,clientClock:clock,requestCount:calls.length,retrySameKey:calls.length>1?calls[0].requestKey===calls[1].requestKey:undefined});
 }
 const result={provider:'Actual API/validator/mapper/PaymentModal TS/TSX under Node VM; hooks/storage/fetch stubs, client clock 2099/2100 and controlled mapper exception. No backend/PostgreSQL/browser E2E.',observations};
 fs.writeFileSync(path.join(__dirname,'receipt-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});`;
new Function('require','__dirname',harness+scenarios)(require,__dirname);
