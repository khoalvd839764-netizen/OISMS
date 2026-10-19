// npm dependency: @electric-sql/pglite. Reads only the handed-off SQL/README; no generator required.
// OISMS_TEST_MODULES may point to a node_modules directory outside the repository.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {randomUUID}=require('node:crypto');
const modules=process.env.OISMS_TEST_MODULES||path.resolve(__dirname,'../tmp/oisms-schema-validation/node_modules');
const {PGlite}=require(path.join(modules,'@electric-sql/pglite'));
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
const passed=[];
const test=async(name,fn)=>{await fn();passed.push(name);console.log('PASS '+name);};
async function rejects(fn,code){let caught;try{await fn();}catch(e){caught=e;}assert(caught,'Expected rejection');if(code)assert.equal(caught.code,code,caught.message);}
(async()=>{
 const db=new PGlite();
 const q=(sql,args=[])=>db.query(sql,args);
 const tx=async fn=>{await db.exec('BEGIN');try{const v=await fn();await db.exec('COMMIT');return v;}catch(e){await db.exec('ROLLBACK');throw e;}};
 async function insert(t,values){const r={id:randomUUID(),...values},cols=Object.keys(r);await q(`INSERT INTO ${t} (${cols.join(',')}) VALUES (${cols.map((_,i)=>'$'+(i+1)).join(',')})`,Object.values(r));return r;}
 await test('Handoff contains SQL and dictionary for all 29 tables',async()=>{
  assert.deepEqual(fs.readdirSync(path.join(root,'docs/database')).sort(),['README.md','oisms_schema.sql']);
  const dictionary=read('docs/database/README.md');
  assert.equal((dictionary.match(/^### \d+\. `/gm)||[]).length,29);
 });
 await test('Complete installation and integrated permissions execute',async()=>{
  const installer=read('docs/database/oisms_schema.sql');
  assert(installer.includes('CREATE DATABASE oisms'));
  assert(installer.includes('\\connect oisms'));
  assert(installer.includes('\\set ON_ERROR_STOP on'));
  const start=installer.indexOf('-- OISMS_SCHEMA_BEGIN');
  assert(start>=0,'Missing schema section marker');
  const body=installer.slice(start);
  await db.exec(body);
 });
 await test('29 tables and 94 FKs',async()=>{
  assert.equal((await q("SELECT count(*)::int n FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'")).rows[0].n,29);
  assert.equal((await q("SELECT count(*)::int n FROM pg_constraint WHERE contype='f'")).rows[0].n,94);
 });
 const tenant=await insert('tenants',{name:'Test tenant'});
 const tenant2=await insert('tenants',{name:'Other tenant'});
 const user=await insert('users',{tenant_id:tenant.id,email:'test@example.test',display_name:'Test',password_hash:'not-a-login',role:'Owner',is_active:false});
 const branch=await insert('branches',{tenant_id:tenant.id,name:'A'});
 const otherBranch=await insert('branches',{tenant_id:tenant2.id,name:'B'});
 const w1=await insert('warehouses',{tenant_id:tenant.id,branch_id:branch.id,name:'W1'});
 const w2=await insert('warehouses',{tenant_id:tenant.id,branch_id:branch.id,name:'W2'});
 const product=await insert('products',{tenant_id:tenant.id,name:'Product'});
 const sku=await insert('skus',{tenant_id:tenant.id,product_id:product.id,code:'TEST',retail_price:150,wholesale_price:140,attributes:JSON.stringify({material:'cotton',size:'L'})});
 const supplier=await insert('suppliers',{tenant_id:tenant.id,name:'Supplier'});
 const key={tenant_id:tenant.id,branch_id:branch.id,sku_id:sku.id};
 let receipt,item,entry;
 await test('Atomic receipt with matching ledger and both stock levels commits',()=>tx(async()=>{
  receipt=await insert('goods_receipts',{tenant_id:tenant.id,branch_id:branch.id,warehouse_id:w1.id,supplier_id:supplier.id,number:'R1',created_by:user.id,status:'Confirmed',confirmed_by:user.id,confirmed_at:new Date().toISOString()});
  item=await insert('goods_receipt_items',{tenant_id:tenant.id,receipt_id:receipt.id,sku_id:sku.id,quantity:10,unit_price:100});
  await insert('inventory_balances',{...key,on_hand:10,average_cost:100,last_sequence:1});
  await insert('warehouse_inventory_balances',{...key,warehouse_id:w1.id,on_hand:10,last_sequence:1});
  entry=await insert('inventory_transactions',{...key,warehouse_id:w1.id,type:'IN',quantity:10,unit_cost:100,balance_after:10,sequence_no:1,receipt_item_id:item.id});
 }));
 await test('Ledger UPDATE/DELETE/TRUNCATE blocked even for schema owner',async()=>{
  for(const sql of ['UPDATE inventory_transactions SET quantity=9','DELETE FROM inventory_transactions','TRUNCATE inventory_transactions CASCADE'])await rejects(()=>db.exec(sql),'23514');
 });
 await test('Standalone branch balance edit rejected at commit and rolled back',async()=>{
  await rejects(()=>tx(()=>q('UPDATE inventory_balances SET on_hand=11')),'23514');
  assert.equal(Number((await q('SELECT on_hand FROM inventory_balances')).rows[0].on_hand),10);
 });
 await test('Changing both balances without a ledger entry still rejected',()=>rejects(()=>tx(async()=>{
  await q('UPDATE inventory_balances SET on_hand=11');await q('UPDATE warehouse_inventory_balances SET on_hand=11');
 }),'23514'));
 await test('Missing warehouse projection rejects ledger and rolls back new source',async()=>{
  await rejects(()=>tx(async()=>{
   const r=await insert('goods_receipts',{tenant_id:tenant.id,branch_id:branch.id,warehouse_id:w1.id,supplier_id:supplier.id,number:'ROLLBACK',created_by:user.id});
   const i=await insert('goods_receipt_items',{tenant_id:tenant.id,receipt_id:r.id,sku_id:sku.id,quantity:1,unit_price:100});
   await insert('inventory_transactions',{...key,warehouse_id:w1.id,type:'IN',quantity:1,unit_cost:100,balance_after:11,sequence_no:2,receipt_item_id:i.id});
  }),'23514');
  assert.equal((await q("SELECT count(*)::int n FROM goods_receipts WHERE number='ROLLBACK'")).rows[0].n,0);
 });
 await test('Invalid BalanceAfter rejected despite matching aggregate totals',()=>rejects(()=>tx(async()=>{
  const a=await insert('inventory_adjustments',{tenant_id:tenant.id,branch_id:branch.id,warehouse_id:w1.id,number:'A1',reason:'Test',created_by:user.id});
  const i=await insert('inventory_adjustment_items',{tenant_id:tenant.id,adjustment_id:a.id,sku_id:sku.id,quantity_delta:1,unit_cost:100});
  await q('UPDATE inventory_balances SET on_hand=11,last_sequence=2');
  await q('UPDATE warehouse_inventory_balances SET on_hand=11,last_sequence=2');
  await insert('inventory_transactions',{...key,warehouse_id:w1.id,type:'IN',quantity:1,unit_cost:100,balance_after:999,sequence_no:2,adjustment_item_id:i.id});
 }),'23514'));
 const order=await insert('orders',{tenant_id:tenant.id,branch_id:branch.id,number:'O1',channel:'POS',request_key:randomUUID()});
 const oi=await insert('order_items',{tenant_id:tenant.id,order_id:order.id,sku_id:sku.id,sku_code_snapshot:'TEST',sku_name_snapshot:'Product',quantity:2,unit_price:150});
 let reservation;
 await test('Reserve with matching branch and warehouse totals commits',()=>tx(async()=>{
  reservation=await insert('reservations',{...key,warehouse_id:w1.id,order_id:order.id,order_item_id:oi.id,quantity:2,expires_at:new Date(Date.now()+3600000).toISOString()});
  await q('UPDATE inventory_balances SET reserved=2');await q('UPDATE warehouse_inventory_balances SET reserved=2');
  await q("UPDATE orders SET status='Reserved' WHERE id=$1",[order.id]);
 }));
 await test('Reservation change without matching balances rejected',()=>rejects(()=>tx(()=>q("UPDATE reservations SET status='Released'")),'23514'));
 await test('Aggregate reservation beyond order line rejected',()=>rejects(()=>tx(async()=>{
  await q('UPDATE reservations SET quantity=3');await q('UPDATE inventory_balances SET reserved=3');await q('UPDATE warehouse_inventory_balances SET reserved=3');
 }),'23514'));
 await test('Reservation cannot exceed on-hand',()=>rejects(()=>tx(()=>q('UPDATE warehouse_inventory_balances SET reserved=11')),'23514'));
 await test('Confirmed order without cost snapshot rejected',()=>rejects(()=>tx(()=>q("UPDATE orders SET status='Confirmed',confirmed_at=now() WHERE id=$1",[order.id])),'23514'));
 await test('Atomic consumption, stock deduction, snapshot and ledger commit',()=>tx(async()=>{
  await q("UPDATE reservations SET status='Consumed' WHERE id=$1",[reservation.id]);
  await q('UPDATE inventory_balances SET on_hand=8,reserved=0,last_sequence=2');
  await q('UPDATE warehouse_inventory_balances SET on_hand=8,reserved=0,last_sequence=2');
  await q('UPDATE order_items SET cost_price=100 WHERE id=$1',[oi.id]);
  await q("UPDATE orders SET status='Confirmed',confirmed_at=now() WHERE id=$1",[order.id]);
  await insert('inventory_transactions',{...key,warehouse_id:w1.id,type:'OUT',quantity:2,unit_cost:100,balance_after:8,sequence_no:2,order_item_id:oi.id});
 }));
 await test('Confirmed cost/quantity and deletion protected',async()=>{
  await rejects(()=>q('UPDATE order_items SET cost_price=101 WHERE id=$1',[oi.id]),'23514');
  await rejects(()=>q('DELETE FROM order_items WHERE id=$1',[oi.id]),'23514');
  await rejects(()=>q("UPDATE orders SET status='Draft',confirmed_at=NULL WHERE id=$1",[order.id]),'23514');
 });
 await test('Cross-tenant warehouse branch rejected',()=>rejects(()=>insert('warehouse_inventory_balances',{...key,branch_id:otherBranch.id,warehouse_id:w2.id}),'23503'));
 await test('Duplicate source rejected',()=>rejects(()=>insert('inventory_transactions',{...key,warehouse_id:w1.id,type:'IN',quantity:10,unit_cost:100,balance_after:18,sequence_no:3,receipt_item_id:item.id}),'23505'));
 await test('Protected tables cannot be truncated or deleted',async()=>{
  for(const t of ['inventory_balances','warehouse_inventory_balances','reservations']){
   await rejects(()=>db.exec('DELETE FROM '+t),'23514');await rejects(()=>db.exec('TRUNCATE '+t+' CASCADE'),'23514');
  }
 });
 await test('Repeatable read stock writes explicitly rejected',()=>rejects(async()=>{
  await db.exec('BEGIN ISOLATION LEVEL REPEATABLE READ');
  try{await q('UPDATE inventory_balances SET reorder_point=1');}finally{await db.exec('ROLLBACK');}
 },'25000'));
 await test('Runtime permissions install and deny ledger edits/trigger bypass',async()=>{
  // Permissions were installed by the single handoff installer.
  await db.exec('SET ROLE oisms_runtime');
  try {
   await tx(()=>q('UPDATE inventory_balances SET reorder_point=2'));
   for(const cmd of ['UPDATE inventory_transactions SET quantity=1','DELETE FROM inventory_transactions','TRUNCATE inventory_transactions CASCADE','ALTER TABLE inventory_transactions DISABLE TRIGGER ALL'])await rejects(()=>db.exec(cmd),'42501');
   await rejects(()=>tx(()=>q('UPDATE inventory_balances SET on_hand=99')),'23514');
  } finally {await db.exec('RESET ROLE');}
 });
 await test('Required order date index exists',async()=>assert.equal((await q("SELECT count(*)::int n FROM pg_indexes WHERE indexname='ix_orders_tenant_created_at'")).rows[0].n,1));
 await test('All 29 required base tables explicitly exist in schema',async()=>{
  const required = [
   'tenants','users','refresh_tokens','branches','warehouses',
   'categories','brands','products','skus','suppliers',
   'goods_receipts','goods_receipt_items','inventory_balances',
   'warehouse_inventory_balances','stock_transfers','stock_transfer_items',
   'stock_counts','stock_count_items','inventory_adjustments',
   'inventory_adjustment_items','orders','order_items','reservations',
   'payments','sales_returns','sales_return_items','inventory_transactions',
   'daily_sales_summaries','replenishment_suggestions'
  ];
  const res = await q("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'");
  const found = new Set(res.rows.map(r => r.table_name));
  assert.equal(required.length, 29);
  for(const t of required) {
   assert(found.has(t), 'Missing required table: ' + t);
  }
 });
 await test('Ledger trigger ledger_append_only exists on inventory_transactions and is enabled',async()=>{
  const res = await q(`
   SELECT count(*)::int AS n
   FROM pg_trigger tg
   JOIN pg_class c ON tg.tgrelid = c.oid
   JOIN pg_namespace n ON c.relnamespace = n.oid
   WHERE n.nspname = 'public'
     AND c.relname = 'inventory_transactions'
     AND tg.tgname = 'ledger_append_only'
     AND tg.tgenabled IN ('O', 'A')
     AND NOT tg.tgisinternal;
  `);
  assert.equal(res.rows[0].n, 1);
 });
 const initScript = read('docker/postgres/init-oisms.sh');
 const doBlockMatch = initScript.match(/DO \$do\$[\s\S]*?\$do\$;/);
 assert(doBlockMatch, 'Could not find DO $do$ block in docker/postgres/init-oisms.sh');
 const doBlockSql = doBlockMatch[0];
 const provisionRole = async (user, pass, dbName = 'postgres', adminUser = 'postgres') => {
   await q('DROP TABLE IF EXISTS _oisms_init_params');
   await q('CREATE TEMPORARY TABLE _oisms_init_params (app_user text NOT NULL, app_password text NOT NULL, target_db text NOT NULL, admin_user text NOT NULL)');
   await q('INSERT INTO _oisms_init_params VALUES ($1, $2, $3, $4)', [user, pass, dbName, adminUser]);
   await db.exec(doBlockSql);
 };

 await test('Runtime provisioning succeeds with complex password containing quotes, semicolons, and dollar signs',async()=>{
  const specialPass = "p@ss'w\"ord;with$pec!al#chars\\and spaces";
  await provisionRole('oisms_test_runtime', specialPass);
  const roleRes = await q("SELECT rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls FROM pg_roles WHERE rolname='oisms_test_runtime'");
  assert.equal(roleRes.rows.length, 1);
  assert.equal(roleRes.rows[0].rolsuper, false);
  assert.equal(roleRes.rows[0].rolcreatedb, false);
  assert.equal(roleRes.rows[0].rolcreaterole, false);
  assert.equal(roleRes.rows[0].rolreplication, false);
  assert.equal(roleRes.rows[0].rolbypassrls, false);
  const memberRes = await q("SELECT pg_has_role('oisms_test_runtime', 'oisms_runtime', 'member') AS is_member");
  assert.equal(memberRes.rows[0].is_member, true);

  // Updating safe existing role should succeed
  await provisionRole('oisms_test_runtime', 'new_p@ss;word"test$2');
 });

 await test('Provisioning rejects unsafe admin roles, inherited roles, and reserved identities',async()=>{
  // 1. Reserved users
  await rejects(() => provisionRole('postgres', 'pass'), 'P0001');
  await rejects(() => provisionRole('oisms_runtime', 'pass'), 'P0001');

  // 2. Direct superuser
  await db.exec("CREATE ROLE unsafe_super WITH SUPERUSER");
  await rejects(() => provisionRole('unsafe_super', 'pass'), 'P0001');

  // 3. Inherited CREATEROLE
  await db.exec("CREATE ROLE parent_createrole WITH CREATEROLE; CREATE ROLE child_inherited; GRANT parent_createrole TO child_inherited;");
  await rejects(() => provisionRole('child_inherited', 'pass'), 'P0001');

  // 4. Table owner
  await db.exec("CREATE ROLE test_tbl_owner; CREATE TABLE public.tbl_owned_test (id int); ALTER TABLE public.tbl_owned_test OWNER TO test_tbl_owner;");
  await rejects(() => provisionRole('test_tbl_owner', 'pass'), 'P0001');
  await db.exec("DROP TABLE public.tbl_owned_test; DROP ROLE test_tbl_owner;");
 });

 await db.close();
 const result={schemaVersion:'0.5',passed:passed.length,tests:passed,limitations:'Embedded PostgreSQL checks; no 50-client concurrency/load test, API, WAC engine, or psql/CREATE DATABASE bootstrap/deployment performed.'};
 console.log(JSON.stringify(result,null,2));
})().catch(e=>{console.error(e);process.exit(1)});
