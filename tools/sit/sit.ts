// `task sit`: Postgres, mock Logto and the app (API, workers and outbox in one process), then every
// Bruno collection: contexts/*/tests/sit, tests/sit (the kernel's own) and each folder in tests/sit-flows.
// Writes one JUnit report per collection under reports/sit/ and stops everything, even on failure.
//
//   bun tools/sit/sit.ts [--only <substring>]    run only collections whose path contains it
//
// Env: SIT_BRUNO_VERSION overrides the pinned @usebruno/cli (the pin below is the default).
import { existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { join, relative } from 'node:path';
import { ROOT, startStack } from './harness';

// Pinned exactly. Bump here, nowhere else.
export const BRUNO_VERSION = '4.3.0';
const REPORTS = join(ROOT, 'reports', 'sit');

const dirs = (p: string): string[] =>
  existsSync(p)
    ? readdirSync(p, { withFileTypes: true })
        .filter(d => d.isDirectory())
        .map(d => d.name)
        .sort()
    : [];

/** A collection is a folder with a bruno.json. */
export function findCollections(root = ROOT): string[] {
  const out: string[] = [];
  const add = (dir: string) => {
    if (existsSync(join(dir, 'bruno.json'))) out.push(dir);
  };
  add(join(root, 'tests', 'sit'));
  for (const d of dirs(join(root, 'tests', 'sit-flows'))) add(join(root, 'tests', 'sit-flows', d));
  for (const d of dirs(join(root, 'contexts'))) add(join(root, 'contexts', d, 'tests', 'sit'));
  return out;
}

async function runBruno(collection: string, vars: Record<string, string>): Promise<number> {
  const name = relative(ROOT, collection).replaceAll('/', '_');
  const args = [
    `@usebruno/cli@${process.env.SIT_BRUNO_VERSION ?? BRUNO_VERSION}`,
    'run',
    '-r',
    '--reporter-junit',
    join(REPORTS, `${name}.xml`),
    ...Object.entries(vars).flatMap(([k, v]) => ['--env-var', `${k}=${v}`]),
  ];
  const p = Bun.spawn(['bunx', ...args], { cwd: collection, stdout: 'inherit', stderr: 'inherit' });
  return await p.exited;
}

async function main(): Promise<number> {
  const only = process.argv.includes('--only') ? process.argv[process.argv.indexOf('--only') + 1] : undefined;
  const collections = findCollections().filter(c => only === undefined || `${relative(ROOT, c)}/`.includes(only));
  if (collections.length === 0) {
    console.error('no Bruno collection found (a collection is a folder with bruno.json)');
    return 1;
  }
  rmSync(REPORTS, { recursive: true, force: true });
  mkdirSync(REPORTS, { recursive: true });
  const stack = await startStack();
  const failed: string[] = [];
  try {
    for (const c of collections) {
      console.log(`\n=== SIT ${relative(ROOT, c)} ===`);
      if ((await runBruno(c, stack.vars)) !== 0) failed.push(relative(ROOT, c));
    }
  } finally {
    await stack.stop();
  }
  console.log(
    `\nSIT: ${collections.length - failed.length}/${collections.length} collections passed; reports in reports/sit/`,
  );
  if (failed.length > 0) console.error(`FAILED: ${failed.join(', ')}`);
  return failed.length === 0 ? 0 : 1;
}

if (import.meta.main) process.exit(await main());
