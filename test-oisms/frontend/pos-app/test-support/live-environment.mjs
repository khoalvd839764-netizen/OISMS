import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
const docker = args => execFileSync('docker', args, { encoding: 'utf8', windowsHide: true }).trim();
export const LIVE_URL = 'http://127.0.0.1:55001';
export async function verifyLiveEnvironment() {
  const backend = JSON.parse(docker(['inspect','oisms_pos_migration_backend']))[0];
  const pg = JSON.parse(docker(['inspect','oisms_pos_migration_pg']))[0];
  assert.equal(backend.Name,'/oisms_pos_migration_backend'); assert.equal(pg.Name,'/oisms_pos_migration_pg');
  const env=Object.fromEntries(backend.Config.Env.map(line=>{const i=line.indexOf('=');return [line.slice(0,i),line.slice(i+1)];}));
  assert.equal(env.Database__Host,'pg'); assert.equal(env.Database__Database,'oisms_pos_migration_test'); assert.equal(env.Database__Username,'oisms_pos_migration_app');
  assert.ok(backend.NetworkSettings.Ports['8080/tcp'].some(p=>p.HostPort==='55001'));
  assert.ok(pg.NetworkSettings.Ports['5432/tcp'].some(p=>p.HostPort==='55433'));
  assert.ok(Object.keys(backend.NetworkSettings.Networks).some(n=>n in pg.NetworkSettings.Networks));
  const ready=await fetch(LIVE_URL+'/health/ready').then(r=>r.json()); assert.ok(ready.isHealthy); assert.equal(ready.currentUser,'oisms_pos_migration_app');
  const query=sql=>docker(['exec','oisms_pos_migration_pg','psql','-X','-v','ON_ERROR_STOP=1','-U','postgres','-d','oisms_pos_migration_test','-At','-c',sql]);
  assert.equal(query('SELECT current_database()'),'oisms_pos_migration_test');
  return {query,database:'oisms_pos_migration_test',backend:LIVE_URL};
}
