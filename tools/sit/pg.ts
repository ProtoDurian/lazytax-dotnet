// Postgres for SIT and the log-file test.
//
//   LT_PG set      use that server (a connection string without Database, as KernelApp reads it).
//                  This is how CI works: Postgres is a service container. Nothing is started or removed.
//   LT_PG not set  start (or reuse) our own container, label lazytax-model=1, name lt-dotnet-sit-ci,
//                  port LT_SIT_PG_PORT (default 55461). `down` removes only that container. Never prune.
//
//   bun tools/sit/pg.ts up | down | url
export interface PgServer {
  // Npgsql connection string without Database, for example "Host=localhost;Port=55461;Username=postgres;Password=postgres"
  server: string;
  // True when this call started the container (so the caller may remove it afterwards).
  started: boolean;
}

const IMAGE = 'postgres:17-alpine';
const NAME = 'lt-dotnet-sit-ci';
const PORT = process.env.LT_SIT_PG_PORT ?? '55461';
const LOCAL = `Host=127.0.0.1;Port=${PORT};Username=postgres;Password=postgres`;

async function docker(...args: string[]): Promise<{ code: number; out: string }> {
  const p = Bun.spawn(['docker', ...args], { stdout: 'pipe', stderr: 'pipe' });
  const [out, err, code] = await Promise.all([new Response(p.stdout).text(), new Response(p.stderr).text(), p.exited]);
  return { code, out: (out + err).trim() };
}

/** Parses an Npgsql connection string into a postgres:// URL for Bun.SQL. */
export function toUrl(conn: string, database = 'postgres'): string {
  const kv = Object.fromEntries(
    conn
      .split(';')
      .filter(p => p.includes('='))
      .map(p => {
        const i = p.indexOf('=');
        return [p.slice(0, i).trim().toLowerCase(), p.slice(i + 1).trim()];
      }),
  );
  const host = kv.host ?? 'localhost';
  const port = kv.port ?? '5432';
  const user = encodeURIComponent(kv.username ?? kv['user id'] ?? 'postgres');
  const pass = encodeURIComponent(kv.password ?? '');
  return `postgres://${user}:${pass}@${host}:${port}/${database}`;
}

export async function waitReady(server: string, ms = 60_000): Promise<void> {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    const sql = new Bun.SQL(toUrl(server), { max: 1, connectionTimeout: 2 });
    try {
      await sql`SELECT 1`;
      await sql.close();
      return;
    } catch {
      await sql.close().catch(() => undefined);
      await Bun.sleep(300);
    }
  }
  throw new Error(`Postgres (${server.replace(/Password=[^;]*/i, 'Password=***')}) did not answer in ${ms / 1000} s`);
}

export async function pgUp(): Promise<PgServer> {
  if (process.env.LT_PG) {
    await waitReady(process.env.LT_PG);
    return { server: process.env.LT_PG, started: false };
  }
  const running = await docker('port', NAME, '5432/tcp');
  let started = false;
  if (running.code !== 0) {
    await docker('rm', '-f', NAME);
    const r = await docker(
      'run',
      '-d',
      '--label',
      'lazytax-model=1',
      '--name',
      NAME,
      '-e',
      'POSTGRES_PASSWORD=postgres',
      '-p',
      `127.0.0.1:${PORT}:5432`,
      IMAGE,
      '-c',
      'max_connections=300',
    );
    if (r.code !== 0) throw new Error(`could not start Postgres: ${r.out}`);
    started = true;
  }
  await waitReady(LOCAL);
  return { server: LOCAL, started };
}

export async function pgDown(): Promise<void> {
  if (process.env.LT_PG) return;
  await docker('rm', '-f', NAME);
}

/** Creates a fresh empty database and returns its Npgsql connection string and a drop function. */
export async function freshDatabase(server: string): Promise<{ connection: string; drop: () => Promise<void> }> {
  const name = `lt_sit_${crypto.randomUUID().replaceAll('-', '').slice(0, 12)}`;
  const admin = new Bun.SQL(toUrl(server), { max: 1 });
  try {
    await admin.unsafe(`CREATE DATABASE "${name}"`);
  } finally {
    await admin.close();
  }
  return {
    connection: `${server};Database=${name}`,
    drop: async () => {
      const a = new Bun.SQL(toUrl(server), { max: 1 });
      try {
        await a.unsafe(`DROP DATABASE IF EXISTS "${name}" WITH (FORCE)`);
      } finally {
        await a.close();
      }
    },
  };
}

if (import.meta.main) {
  const cmd = process.argv[2];
  if (cmd === 'up') console.log((await pgUp()).server);
  else if (cmd === 'down') await pgDown();
  else if (cmd === 'url') {
    const r = await docker('port', NAME, '5432/tcp');
    if (process.env.LT_PG || r.code === 0) console.log(process.env.LT_PG ?? LOCAL);
    else process.exit(1);
  } else {
    console.error('usage: bun tools/sit/pg.ts up | down | url');
    process.exit(2);
  }
}
