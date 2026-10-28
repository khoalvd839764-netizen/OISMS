import test from 'node:test';
import assert from 'node:assert/strict';
import { verifyIsolatedBackend } from './test-support/isolated-backend.mjs';

function fixture() {
  const backend = { Name: '/oisms_test_backend', State: { Running: true }, Config: { Env: ['Database__Host=oisms_test_pg_check', 'Database__Database=oisms', 'Database__Username=oisms_test_app'] }, NetworkSettings: { Ports: { '8080/tcp': [{ HostPort: '5001' }] }, Networks: { isolated: {} } } };
  const pg = { Name: '/oisms_test_pg_check', State: { Running: true }, NetworkSettings: { Ports: { '5432/tcp': [{ HostPort: '5433' }] }, Networks: { isolated: {} } } };
  const proof = { database: 'oisms', user: 'oisms_test_app', markers: [{ name: 'run_test_b02_v17' }] };
  return { backend, pg, proof, io: { inspect: name => name === 'oisms_test_backend' ? backend : pg, query: () => JSON.stringify(proof), ready: async () => ({ isHealthy: true, currentUser: 'oisms_test_app' }) } };
}
test('B03-F04: known isolated binding succeeds', async () => { const f = fixture(); assert.equal((await verifyIsolatedBackend({}, f.io)).database, 'oisms'); });
for (const port of ['not-a-port', 5432, 5434]) test(`B03-F04: bad port ${port} rejected`, async () => { const f = fixture(); await assert.rejects(verifyIsolatedBackend({ postgresPort: port }, f.io)); });
test('B03-F04: same role on another backend DB rejected', async () => { const f = fixture(); f.backend.Config.Env[1] = 'Database__Database=other_db'; await assert.rejects(verifyIsolatedBackend({}, f.io), /database mismatch/); });
test('B03-F04: marker in unrelated container cannot authorize backend', async () => { const f = fixture(); f.backend.Config.Env[0] = 'Database__Host=demo_db'; await assert.rejects(verifyIsolatedBackend({}, f.io), /marked database/); });
test('B03-F04: Docker port binding mismatch rejected', async () => { const f = fixture(); f.backend.NetworkSettings.Ports['8080/tcp'][0].HostPort = '5000'; await assert.rejects(verifyIsolatedBackend({}, f.io), /Published ports/); });
test('B03-F04: role alone cannot substitute for marker', async () => { const f = fixture(); f.proof.markers = [{ name: 'wrong' }]; await assert.rejects(verifyIsolatedBackend({}, f.io), /marker/); });
test('B03-F04: demo endpoint rejected before reads', async () => { await assert.rejects(verifyIsolatedBackend({ backendUrl: 'http://localhost:5000' }, { inspect() { throw Error('Must not read'); } }), /Unverified backend/); });
