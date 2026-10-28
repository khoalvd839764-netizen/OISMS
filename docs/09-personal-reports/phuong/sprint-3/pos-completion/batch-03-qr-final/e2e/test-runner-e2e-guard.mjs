/** Real browser + backend + PostgreSQL acceptance. Guard runs before all writes.
 * Original AC mapping is in e2e-matrix.md. SQL assertions are read-only.
 */
import { runB03Live } from '../../../../../../../src/frontend/pos-app/test-b03-live.mjs';
await runB03Live();
