/**
 * Đường dẫn tới source POS (src/frontend/pos-app) khi test nằm trong test-oisms.
 * typescript và các gói khác được nạp từ node_modules của pos-app, nên chỉ cần
 * `npm ci` trong pos-app như trước.
 */
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

export const APP_URL = new URL('../../../../src/frontend/pos-app/', import.meta.url);
export const APP_DIR = fileURLToPath(APP_URL);
export const appRequire = createRequire(new URL('package.json', APP_URL));
export const ts = appRequire('typescript');
