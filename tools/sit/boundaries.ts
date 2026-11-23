// A readable boundary check over contexts/. The compiler is the real gate (a context's project only
// references platform, its own contract and the contracts in `needs:`); this names the offender plainly.
//
//   1. every csproj under contexts/ is exactly `<Project Sdk="Microsoft.NET.Sdk" />`
//   2. a context's code never names another context's non-contract namespace (Ctx.<Other>.<not Contract>)
//   3. a context's src never uses a contract namespace it did not list in `needs:`
//   4. domain/ imports no EF, Npgsql, ASP.NET, HTTP or IO (LYR001 is the real gate; this is the message)
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { ROOT } from './harness';

const CONTEXTS = join(ROOT, 'contexts');
const problems: string[] = [];
const cap = (s: string) => s.slice(0, 1).toUpperCase() + s.slice(1);

function walk(dir: string, out: string[] = []): string[] {
  if (!existsSync(dir)) return out;
  for (const e of readdirSync(dir)) {
    if (['bin', 'obj', 'node_modules'].includes(e)) continue;
    const p = join(dir, e);
    if (statSync(p).isDirectory()) walk(p, out);
    else out.push(p);
  }
  return out;
}

const names = readdirSync(CONTEXTS, { withFileTypes: true })
  .filter(d => d.isDirectory())
  .map(d => d.name)
  .sort();

for (const name of names) {
  const dir = join(CONTEXTS, name);
  const yamlPath = join(dir, 'context.yaml');
  const yaml = existsSync(yamlPath) ? readFileSync(yamlPath, 'utf8') : '';
  const needsLine = /^needs:\s*\[(.*)\]/m.exec(yaml)?.[1] ?? '';
  const needs = new Set(
    needsLine
      .split(',')
      .map(s => s.trim().split('.')[0] ?? '')
      .filter(Boolean),
  );
  for (const file of walk(dir)) {
    const rel = relative(ROOT, file);
    if (file.endsWith('.csproj')) {
      const body = readFileSync(file, 'utf8').trim();
      if (body !== '<Project Sdk="Microsoft.NET.Sdk" />')
        problems.push(
          `${rel}: a context project must be exactly <Project Sdk="Microsoft.NET.Sdk" /> (references come from needs:)`,
        );
      continue;
    }
    if (!file.endsWith('.cs')) continue;
    const text = readFileSync(file, 'utf8');
    const inSrc = file.includes(`${join(dir, 'src')}/`);
    for (const other of names) {
      if (other === name) continue;
      const ns = `Ctx.${cap(other)}`;
      const refs = [...text.matchAll(new RegExp(`\\b${ns}\\.([A-Za-z]+)`, 'g'))];
      for (const m of refs) {
        if (m[1] !== 'Contract')
          problems.push(
            `${rel}: uses ${ns}.${m[1]}, which is inside context '${other}'. Only ${ns}.Contract is public`,
          );
        else if (inSrc && !needs.has(other))
          problems.push(
            `${rel}: uses ${ns}.Contract but context.yaml of '${name}' does not list ${other}.v1 in needs:`,
          );
      }
    }
    if (inSrc && file.includes('/domain/')) {
      const bad =
        /^using\s+(Microsoft\.EntityFrameworkCore|Npgsql|Microsoft\.AspNetCore|System\.Net\.Http|System\.IO)\b.*$/m.exec(
          text,
        );
      if (bad) problems.push(`${rel}: domain/ must not use \`${bad[0].trim()}\` (layer rule LYR001)`);
    }
  }
}

if (problems.length > 0) {
  console.error(`boundaries: ${problems.length} problem(s)\n${problems.map(p => `  - ${p}`).join('\n')}`);
  process.exit(1);
}
console.log(`boundaries: ${names.length} contexts clean (${names.join(', ')})`);
