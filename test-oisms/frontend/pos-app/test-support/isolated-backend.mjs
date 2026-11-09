import { execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';

const docker = args => execFileSync('docker', args, { encoding: 'utf8', windowsHide: true }).trim();
const envMap = container => Object.fromEntries((container.Config?.Env || []).map(e => { const i = e.indexOf('='); return [e.slice(0, i), e.slice(i + 1)]; }));
const exposed = (container, internal, external) => (container.NetworkSettings?.Ports?.[internal] || []).some(p => Number(p.HostPort) === external);

/** Read-only proof tying the requested HTTP port to its container and marked DB. */
export async function verifyIsolatedBackend(options = {}, io = {}) {
  const backendUrl = options.backendUrl || process.env.POS_TEST_BACKEND_URL || 'http://localhost:5001';
  const pgPort = Number(options.postgresPort ?? process.env.POSTGRES_TEST_PORT ?? 5433);
  const backendName = options.backendContainer || process.env.POS_TEST_BACKEND_CONTAINER || 'oisms_test_backend';
  const pgName = options.postgresContainer || process.env.POSTGRES_TEST_CONTAINER || 'oisms_test_pg_check';
  const marker = options.marker || 'run_test_b02_v17';
  const url = new URL(backendUrl);
  assert.ok(url.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(url.hostname) && Number(url.port) === 5001 && url.pathname === '/' && !url.username && !url.password, 'Unverified backend URL; expected isolated loopback5001');
  assert.ok(Number.isInteger(pgPort) && pgPort === 5433, 'Unverified PostgreSQL port; expected isolated5433');
  assert.equal(backendName, 'oisms_test_backend', 'Unexpected backend container');
  assert.equal(pgName, 'oisms_test_pg_check', 'Unexpected PostgreSQL container');
  const inspect = io.inspect || (name => JSON.parse(docker(['inspect', name]))[0]);
  const backend = inspect(backendName), pg = inspect(pgName);
  assert.equal(backend.Name, '/' + backendName); assert.equal(pg.Name, '/' + pgName);
  assert.ok(backend.State?.Running && pg.State?.Running, 'Test containers must be running');
  assert.ok(exposed(backend, '8080/tcp', 5001) && exposed(pg, '5432/tcp', pgPort), 'Published ports do not match target containers');
  const env = envMap(backend);
  assert.equal(env.Database__Host, pgName, 'Backend does not use the marked database container');
  assert.equal(env.Database__Database, 'oisms', 'Backend database mismatch');
  assert.equal(env.Database__Username, 'oisms_test_app', 'Backend role mismatch');
  assert.equal(Number(env.Database__Port || 5432), 5432, 'Backend internal database port mismatch');
  const networks = Object.keys(backend.NetworkSettings?.Networks || {});
  assert.ok(networks.some(n => n in (pg.NetworkSettings?.Networks || {})), 'Backend and database do not share a Docker network');
  const query = io.query || (sql => docker(['exec', pgName, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'oisms_test_app', '-d', 'oisms', '-At', '-c', sql]));
  const proof = JSON.parse(query("SELECT json_build_object('database',current_database(),'user',current_user,'markers',(SELECT json_agg(row_to_json(m)) FROM public._oisms_test_fixture_marker m));"));
  assert.equal(proof.database, 'oisms'); assert.equal(proof.user, 'oisms_test_app');
  assert.ok(Array.isArray(proof.markers) && proof.markers.some(row => Object.values(row).includes(marker)), 'Test fixture marker missing or wrong');
  const ready = io.ready || (async target => { const r = await fetch(target + '/health/ready'); assert.ok(r.ok); return r.json(); });
  const readiness = await ready(url.origin);
  assert.ok(readiness.isHealthy && readiness.currentUser === proof.user, 'Backend readiness does not match the verified database role');
  return { backendUrl: url.origin, backendContainer: backendName, postgresContainer: pgName, postgresPort: pgPort, database: proof.database, user: proof.user, marker, query };
}

export function sqlUuid(value) {
  assert.match(value, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
  return `'${value}'`;
}
