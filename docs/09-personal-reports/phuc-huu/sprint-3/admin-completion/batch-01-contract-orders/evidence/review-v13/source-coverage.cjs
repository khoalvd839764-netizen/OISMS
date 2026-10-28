const fs = require('node:fs');
const path = require('node:path');
const originalRead = fs.readFileSync;
const components = new Set();
fs.readFileSync = function(file, ...args) {
  if (typeof file === 'string' && /\.(tsx|jsx)$/.test(file)) components.add(path.resolve(file));
  return originalRead.call(this, file, ...args);
};
process.on('exit', () => console.log('REVIEW_COMPONENT_FILES_READ=' + JSON.stringify([...components])));
require(path.resolve(process.cwd(), 'scratch/test_regression_v12.cjs'));
