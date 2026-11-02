const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const {createRequire} = require('node:module');
const root = path.resolve('src/frontend/pos-app');
const appRequire = createRequire(path.join(root,'package.json'));
const ts = appRequire('typescript');
const React = appRequire('react');
const {renderToStaticMarkup} = appRequire('react-dom/server');
const {chromium} = require('C:/Users/Jacki Khoa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const source = fs.readFileSync(path.join(root,'src/components/ReceiptPrintModal.tsx'),'utf8');
const css = fs.readFileSync(path.join(root,'src/index.css'),'utf8');
const js = ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
const receipt = {receiptNumber:'SERVER-REVIEW-001',createdAt:'27/10/2026 12:00',tenantName:'Reviewer fixture shop',branchName:'Branch A',cashierName:'Cashier A',items:[{name:'Server snapshot long product name review fixture',sku:'SKU-A',quantity:2,unitPrice:100,subtotal:200}],totalAmount:200,paymentMethod:'Cash',amountGiven:500,changeDue:300,isDraft:false};
(async()=>{
  const browser=await chromium.launchPersistentContext(path.join(__dirname,'edge-profile'),{executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true});
  try {
    const observations=[];
    for(const width of ['58mm','80mm']) for(const draft of [false,true]) {
      const mod={exports:{}};
      vm.runInNewContext(js,{module:mod,exports:mod.exports,require:appRequire,localStorage:{getItem:()=>width,setItem(){}}});
      const markup=renderToStaticMarkup(React.createElement(mod.exports.ReceiptPrintModal,{receipt:{...receipt,isDraft:draft},onClose(){}}));
      const html='<!DOCTYPE html><html><meta charset="utf-8"><style>'+css+'</style><body><div id="root">'+markup+'</div></body></html>';
      const stem=width+'-'+(draft?'draft':'official');
      fs.writeFileSync(path.join(__dirname,'production-component-'+stem+'.html'),html);
      const page=await browser.newPage();
      await page.setContent(html);
      const visibleBefore=await page.locator('.receipt-paper').isVisible();
      await page.emulateMedia({media:'print'});
      const result=await page.evaluate(()=>({
        receiptVisible:document.querySelector('.receipt-paper').getBoundingClientRect().width>0,
        ancestorDisplay:getComputedStyle(document.querySelector('.no-print-bg')).display,
        paperRectWidth:document.querySelector('.receipt-paper').getBoundingClientRect().width,
        pageSizeSyntaxSupported:CSS.supports('size','58mm auto'),
        parsedPageRules:[...document.styleSheets].flatMap(s=>[...s.cssRules]).filter(r=>r.type===CSSRule.MEDIA_RULE).flatMap(r=>[...r.cssRules]).filter(r=>r.type===CSSRule.PAGE_RULE).map(r=>r.cssText),
      }));
      await page.pdf({path:path.join(__dirname,'actual-print-'+stem+'.pdf'),preferCSSPageSize:true,printBackground:true,displayHeaderFooter:false});
      observations.push({width,draft,visibleBeforePrint:visibleBefore,...result});
      await page.close();
    }
    const output={provider:'Installed Edge headless + Playwright; production ReceiptPrintModal rendered by real ReactDOMServer and exact current index.css. No alternate receipt template; no Tailwind CDN or backend/DB required for ancestor display:none/page-size checks.',observations};
    fs.writeFileSync(path.join(__dirname,'browser-results.json'),JSON.stringify(output,null,2));
    console.log(JSON.stringify(output,null,2));
  }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
