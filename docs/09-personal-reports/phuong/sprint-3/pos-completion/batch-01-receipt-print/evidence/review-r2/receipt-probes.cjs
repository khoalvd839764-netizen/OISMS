const fs=require('node:fs');const path=require('node:path');
const harness=fs.readFileSync(path.join(__dirname,'harness.cjs'),'utf8');
const scenarios=String.raw`
(async()=>{
 const validReceipt={orderId:'55555555-5555-4555-8555-555555555555',orderNumber:'SERVER-1',receiptNumber:'SERVER-1',branchId:branchA,branchName:'A',branchAddress:null,branchPhone:null,cashierName:'A',createdAt:'2026-10-27T07:00:00Z',paymentMethod:'Cash',totalAmount:100,amountGiven:100,changeDue:0,items:[{skuId:props.items[0].variantId,skuCode:'SKU-A',productName:'Server renamed product',quantity:1,unitPrice:100,subtotal:100}]};
 for(const kind of ['valid-snapshot','empty-item','bad-metadata','bad-date','missing-date','null-date','mapper-throws']){
  let printed=[];const calls=[];
  let receipt={...validReceipt};
  if(kind==='empty-item')receipt={...receipt,items:[{}]};
  if(kind==='bad-metadata')receipt.branchAddress=42;
  if(kind==='bad-date')receipt.createdAt='invalid-date';
  if(kind==='missing-date')delete receipt.createdAt;
  if(kind==='null-date')receipt.createdAt=null;
  const env=environment(async(url,init)=>{calls.push(JSON.parse(init.body));return response(200,{orderNumber:'SERVER-1',receipt});});
  const api=apiFor(env);
  const usedApi=kind==='mapper-throws'?{...api,mapServerReceiptToReceiptData(){throw new Error('Injected mapping failure after API validation')}}:api;
  const m=modal(env,usedApi,{...props,currentUser:userA,onSuccessAndPrint:r=>printed.push(r)});
  await checkout(m);
  const pendingAfterFirst=api.getPendingCheckout(userA,branchA);
  if(kind==='valid-snapshot') {assert.equal(printed[0].items[0].name,'Server renamed product');assert.equal(pendingAfterFirst,null);}
  else if(kind==='missing-date'||kind==='null-date') {assert.equal(printed.length,1);assert.equal(pendingAfterFirst,null);assert.ok(printed[0].createdAt);}
  else {assert.equal(printed.length,0);assert.ok(pendingAfterFirst);await checkout(m);assert.equal(calls[0].requestKey,calls[1].requestKey);assert.equal(JSON.stringify(calls[0]),JSON.stringify(calls[1]));}
  observations.push({kind,printedCount:printed.length,pendingKeptAfterFirst:Boolean(pendingAfterFirst),sourceCreatedAt:receipt.createdAt??null,mappedCreatedAt:printed[0]?.createdAt??null,requestCount:calls.length,retrySameKey:calls.length>1?calls[0].requestKey===calls[1].requestKey:undefined});
 }
 const result={provider:'Actual API/validator/mapper/PaymentModal TS/TSX in Node VM; hooks/storage/fetch stubs, mapping fault injection only in mapper-throws control. No backend/PostgreSQL.',observations};
 fs.writeFileSync(path.join(__dirname,'receipt-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});`;
new Function('require','__dirname',harness+scenarios)(require,__dirname);
