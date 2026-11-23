// The black-box stack shared by `task sit` and the log-file test: Postgres, a fresh database,
// mock Logto and the real app (the built kernel, one process: API, workers and outbox dispatcher).
// Nothing runs in-process with the test; the app is a child process reached over HTTP.
//
//   const stack = await startStack({ logFile });  // ... talk to stack.baseUrl ...  await stack.stop();
//
// `stop` always runs the full teardown (app, mock Logto, database, our Postgres container if we
// started it). Callers put it in a finally block. Build first: `task sit:build` (kernel + MockLogto, Release).
import { existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { freshDatabase, pgDown, pgUp } from './pg';

export const ROOT = resolve(import.meta.dir, '..', '..');
const KERNEL_DIR = join(ROOT, 'kernel', 'bin', 'Release', 'net10.0');
const LOGTO_DIR = join(ROOT, 'tools', 'MockLogto', 'bin', 'Release', 'net10.0');
const AUDIENCE = 'https://api.lazytax.local';

export interface Stack {
  baseUrl: string;
  logtoUrl: string;
  // Variables every Bruno collection receives (see docs/sit.md).
  vars: Record<string, string>;
  // Mint a token for any sub through mock Logto's POST /token.
  mint(sub: string, o?: { scopes?: string[]; claims?: Record<string, unknown>; expiresIn?: number }): Promise<string>;
  // The app's stdout+stderr file, for diagnosing a failed start.
  appOutput: string;
  stop(): Promise<void>;
}

export interface StackOptions {
  // Sets LOG_FILE for the app.
  logFile?: string;
  // Extra env for the app, for example { Workers__Enabled: 'false' }.
  env?: Record<string, string>;
  healthPath?: string;
}

async function freePort(): Promise<number> {
  const s = Bun.serve({ port: 0, hostname: '127.0.0.1', fetch: () => new Response('') });
  const port = s.port;
  await s.stop(true);
  if (port === undefined) throw new Error('could not find a free port');
  return port;
}

async function waitHttp(url: string, alive: () => boolean, ms: number): Promise<void> {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (!alive()) throw new Error('the process exited before it answered');
    try {
      const res = await fetch(url, { signal: AbortSignal.timeout(1500) });
      if (res.status < 500) return;
    } catch {
      // not up yet
    }
    await Bun.sleep(250);
  }
  throw new Error(`${url} did not answer in ${ms / 1000} s`);
}

function spawnDll(dir: string, dll: string, env: Record<string, string>, out: string) {
  const path = join(dir, dll);
  if (!existsSync(path)) throw new Error(`${path} is missing. Run \`task sit:build\` first.`);
  return Bun.spawn(['dotnet', path], { cwd: dir, env, stdout: Bun.file(out), stderr: Bun.file(out) });
}

async function kill(p: ReturnType<typeof Bun.spawn>): Promise<void> {
  p.kill('SIGTERM');
  const done = await Promise.race([p.exited.then(() => true), Bun.sleep(5000).then(() => false)]);
  if (!done) p.kill('SIGKILL');
}

export async function startStack(opts: StackOptions = {}): Promise<Stack> {
  const cleanups: Array<() => Promise<void>> = [];
  const stop = async () => {
    for (const c of cleanups.reverse()) await c().catch(e => console.error('teardown step failed:', e));
    cleanups.length = 0;
  };
  try {
    const tmp = mkdtempSync(join(tmpdir(), 'lazytax-sit-'));
    cleanups.push(async () => rmSync(tmp, { recursive: true, force: true }));
    mkdirSync(tmp, { recursive: true });

    const pg = await pgUp();
    if (pg.started && process.env.SIT_KEEP_PG !== '1') cleanups.push(pgDown);
    const db = await freshDatabase(pg.server);
    cleanups.push(db.drop);

    // Mock Logto. The app pins its issuer, so use the same host name for both.
    const logtoPort = await freePort();
    const logtoUrl = `http://localhost:${logtoPort}`;
    const logtoOut = join(tmp, 'logto.out');
    const logto = spawnDll(
      LOGTO_DIR,
      'LazyTax.MockLogto.dll',
      { ...(process.env as Record<string, string>), ASPNETCORE_URLS: logtoUrl, Audience: AUDIENCE },
      logtoOut,
    );
    cleanups.push(() => kill(logto));
    let logtoExited = false;
    void logto.exited.then(() => {
      logtoExited = true;
    });
    await waitHttp(`${logtoUrl}/oidc/jwks`, () => !logtoExited, 60_000);

    const mint: Stack['mint'] = async (sub, o = {}) => {
      const res = await fetch(`${logtoUrl}/token`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ sub, scope: (o.scopes ?? []).join(' '), claims: o.claims, expiresIn: o.expiresIn }),
      });
      if (!res.ok) throw new Error(`mock Logto /token answered ${res.status}`);
      return ((await res.json()) as { access_token: string }).access_token;
    };

    // The app.
    const port = await freePort();
    const baseUrl = `http://127.0.0.1:${port}`;
    const appOutput = join(tmp, 'app.out');
    const env: Record<string, string> = {
      ...(process.env as Record<string, string>),
      ASPNETCORE_URLS: baseUrl,
      ConnectionStrings__Main: db.connection,
      Auth__Issuer: `${logtoUrl}/oidc`,
      Auth__Audience: AUDIENCE,
      Workers__Enabled: 'true',
      ...(opts.logFile ? { LOG_FILE: opts.logFile } : {}),
      ...opts.env,
    };
    // SIT_APP_CMD swaps the app for a stand-in (used to test this harness on its own).
    const app = process.env.SIT_APP_CMD
      ? Bun.spawn(process.env.SIT_APP_CMD.split(' '), {
          cwd: ROOT,
          env,
          stdout: Bun.file(appOutput),
          stderr: Bun.file(appOutput),
        })
      : spawnDll(KERNEL_DIR, 'LazyTax.Kernel.dll', env, appOutput);
    cleanups.push(() => kill(app));
    let exited = false;
    void app.exited.then(() => {
      exited = true;
    });
    try {
      await waitHttp(`${baseUrl}${opts.healthPath ?? '/health'}`, () => !exited, 90_000);
    } catch (e) {
      const out = await Bun.file(appOutput)
        .text()
        .catch(() => '');
      throw new Error(`${(e as Error).message}\n--- app output ---\n${out.slice(-4000)}`);
    }

    const subs = {
      subA: 'sit-user-a',
      subB: 'sit-user-b',
      subC: 'sit-user-c',
      subD: 'sit-user-d',
      subAdmin: 'sit-admin',
    };
    // Sign-up (POST /api/v1/User) reads username, email and email_verified from the id token (LazyTax TokenDataExtractor).
    const user = (sub: string) => ({
      username: sub.replaceAll('-', ''),
      email: `${sub}@example.test`,
      email_verified: true,
    });
    const vars: Record<string, string> = {
      baseUrl,
      logtoUrl,
      ...subs,
      tokenA: await mint(subs.subA, { claims: user(subs.subA) }),
      tokenB: await mint(subs.subB, { claims: user(subs.subB) }),
      tokenC: await mint(subs.subC, { claims: user(subs.subC) }),
      tokenD: await mint(subs.subD, { claims: user(subs.subD) }),
      tokenAdmin: await mint(subs.subAdmin, { scopes: ['admin'], claims: user(subs.subAdmin) }),
      tokenExpired: await mint(subs.subA, { expiresIn: -600 }),
    };
    return { baseUrl, logtoUrl, vars, mint, appOutput, stop };
  } catch (e) {
    await stop();
    throw e;
  }
}
