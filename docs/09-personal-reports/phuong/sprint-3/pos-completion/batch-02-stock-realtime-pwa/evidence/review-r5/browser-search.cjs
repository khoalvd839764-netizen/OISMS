const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),assert=require('node:assert/strict');
const {chromium}=require('C:/Users/Jacki Khoa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const dist=path.resolve('src/frontend/pos-app/dist');
const branch='11111111-1111-4111-8111-111111111111',variant='33333333-3333-4333-8333-333333333333';
const user={id:'user-review',tenantId:'tenant-review',email:'review@fixture.local',fullName:'Reviewer fixture',tenantName:'Fixture shop'};
const server=http.createServer((req,res)=>{let file=path.resolve(dist,'.'+new URL(req.url,'http://localhost').pathname);if(!file.startsWith(dist+path.sep)&&file!==dist){res.writeHead(403).end();return}if(!fs.existsSync(file)||fs.statSync(file).isDirectory())file=path.join(dist,'index.html');const mime={'.html':'text/html','.js':'text/javascript','.css':'text/css','.svg':'image/svg+xml','.webmanifest':'application/manifest+json'};res.setHeader('Content-Type',mime[path.extname(file)]||'application/octet-stream');res.end(fs.readFileSync(file))});
(async()=>{
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const base='http://127.0.0.1:'+server.address().port;
 const context=await chromium.launchPersistentContext(path.join(__dirname,'edge-profile'),{executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true,viewport:{width:1440,height:1000}});
 try{
  await context.addInitScript(u=>{localStorage.setItem('pos_user',JSON.stringify(u));localStorage.setItem('pos_access_token','fixture-only-token')},user);
  await context.route('**/hubs/**',r=>r.abort());
  let available=10;const requests=[];
  const item=()=>({variantId:variant,productId:'product-review',sku:'SKU-A',productName:'Product A',variantName:'',barcode:'BAR-A',retailPrice:100,available,onHand:available,reserved:0});
  await context.route('**/api/**',async route=>{
   const url=new URL(route.request().url());requests.push({method:route.request().method(),path:url.pathname,availableAtRequest:available});let data,status=200;
   if(url.pathname==='/api/branches')data=[{id:branch,name:'Branch A',isActive:true}];
   else if(url.pathname==='/api/pos/search')data=[item()];
   else if(url.pathname.startsWith('/api/pos/barcode/'))data=item();
   else if(url.pathname==='/api/pos/checkout'){
    available=8;status=201;data={success:true,orderNumber:'SERVER-CHECKOUT-1',receipt:{orderId:'55555555-5555-4555-8555-555555555555',receiptNumber:'SERVER-CHECKOUT-1',orderNumber:'SERVER-CHECKOUT-1',branchId:branch,branchName:'Branch A',cashierName:user.fullName,totalAmount:200,paymentMethod:'Cash',amountGiven:200,changeDue:0,createdAt:'2026-10-28T00:00:00Z',items:[{skuId:variant,skuCode:'SKU-A',productName:'Product A',quantity:2,unitPrice:100,subtotal:200}]}};
   }else{status=404;data={detail:'Fixture route not defined'}}
   await route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
  });
  const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(base,{waitUntil:'networkidle'});
  const search=page.getByPlaceholder(/Tìm tên/);
  await search.fill('SKU-A');await page.getByText('SKU: SKU-A',{exact:true}).click();
  available=8; // Server stock changes externally while this POS has Hub offline.
  const refreshed=page.waitForResponse(r=>r.url().includes('/api/pos/search'));
  await search.fill('SKU-A');await refreshed;await page.getByText('SKU: SKU-A',{exact:true}).click();
  await page.locator('input[type=number]').waitFor();
  const max=await page.locator('input[type=number]').first().getAttribute('max');
  assert.equal(available,8);assert.equal(max,'8');
  assert.ok(requests.some(r=>r.path==='/api/pos/search'&&r.availableAtRequest===8));
  await page.screenshot({path:path.join(__dirname,'browser-search-fixed.png'),fullPage:true});
  const result={provider:'Real production dist in installed Edge; fixture REST APIs, Hub offline. Server changes after initial search; no real backend/DB writes.',serverAvailable:available,cartInputMaxAfterFreshSearch:max,requests,pageErrors:errors};
  fs.writeFileSync(path.join(__dirname,'browser-search-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }finally{await context.close();await new Promise(r=>server.close(r))}
})().catch(e=>{console.error(e);server.close();process.exitCode=1});

