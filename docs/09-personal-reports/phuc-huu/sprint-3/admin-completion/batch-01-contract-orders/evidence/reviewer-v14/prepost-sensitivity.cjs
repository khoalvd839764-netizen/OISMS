const fs = require('node:fs');
const path = require('node:path');
const original = fs.readFileSync;
let replacements = 0;
fs.readFileSync = function(file, ...args) {
  let body = original.call(this, file, ...args);
  if (typeof file === 'string' && file.replaceAll('\\','/').endsWith('/src/pages/OrdersPage.tsx')) {
    const text = body.toString();
    const before = 'isUnknownState: true, // In-flight = unknown until definite server response';
    if (text.includes(before)) {
      const changed = text.replace(before, 'isUnknownState: false, // REVIEW MUTATION: wrong state before POST');
      replacements++;
      body = typeof body === 'string' ? changed : Buffer.from(changed);
    }
  }
  return body;
};
process.on('exit', () => {
  console.log('REVIEW_PREPOST_MUTATION_COUNT=' + replacements);
  if (replacements !== 1) process.exitCode = 2;
});
require(path.resolve(process.cwd(), 'scratch/test_regression_v12.cjs'));
