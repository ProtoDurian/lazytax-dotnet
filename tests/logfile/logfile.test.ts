// The log-file test: start the real app (the built kernel) with LOG_FILE set, make requests, then
// assert on the file. One JSON line per request with context, route or tool, sub, request id, status
// and duration; no token, email or request body anywhere; an error request logs at error level with
// its problem type; an MCP tools/call is logged with its tool name.
//
// The app does not read a caller-supplied request id, so requests run one after another and the
// n-th new line in the file belongs to the n-th request. Error responses also carry the line's
// request id as `traceId` in the problem body, which proves the correlation.
import { afterAll, beforeAll, describe, expect, it } from 'bun:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { type Stack, startStack } from '../../tools/sit/harness';

const EMAIL = 'secret.person@example.com';
const BODY_MARKER = 'BODY-MARKER-9f3c1';
const SUB = 'logfile-user';

interface Line {
  time: string;
  level: string;
  context?: string;
  method?: string;
  route?: string;
  tool?: string | null;
  sub?: string | null;
  requestId?: string;
  status?: number;
  durationMs?: number;
  problem?: string | null;
  [k: string]: unknown;
}

let stack: Stack;
let dir: string;
let logFile: string;
let token: string;
// What each request answered, and the log line it produced, by label.
const sent: Record<string, { status: number; body: string; line?: Line }> = {};
const order: string[] = [];
let toolName = 'logfile.unknown';
let lineCountBefore = 0;

const readRaw = () => readFileSync(logFile, 'utf8');
const lines = (): Line[] =>
  readRaw()
    .split('\n')
    .filter(l => l.trim() !== '')
    .map(l => JSON.parse(l) as Line);

async function call(label: string, path: string, init: RequestInit = {}): Promise<void> {
  const res = await fetch(`${stack.baseUrl}${path}`, init);
  sent[label] = { status: res.status, body: await res.text() };
  order.push(label);
}

beforeAll(async () => {
  dir = mkdtempSync(join(tmpdir(), 'lazytax-logfile-'));
  logFile = join(dir, 'app.log');
  stack = await startStack({ logFile });
  // A token whose claims carry an email: it must never reach the file.
  token = await stack.mint(SUB, { claims: { email: EMAIL } });
  const auth = { authorization: `Bearer ${token}` };
  const json = { ...auth, 'content-type': 'application/json', accept: 'application/json, text/event-stream' };
  const mcp = (id: number, method: string, params?: unknown) =>
    JSON.stringify({ jsonrpc: '2.0', id, method, ...(params ? { params } : {}) });

  // The tool to call: the first the app lists (this request is logged too, so it is counted below).
  const list = await fetch(`${stack.baseUrl}/mcp`, { method: 'POST', headers: json, body: mcp(1, 'tools/list') });
  const listed = (await list.json().catch(() => ({}))) as { result?: { tools?: Array<{ name: string }> } };
  toolName = listed.result?.tools?.[0]?.name ?? toolName;

  lineCountBefore = lines().length;
  await call('get', '/health', { headers: auth });
  await call('post', `/api/v1/Vacation/${SUB}`, {
    method: 'POST',
    headers: json,
    body: JSON.stringify({ startDate: BODY_MARKER, endDate: BODY_MARKER, note: BODY_MARKER }),
  });
  await call('anon', '/api/v1/User/Me');
  await call('missing', `/api/v1/DoesNotExist/${SUB}`, { headers: auth });
  await call('mcp', '/mcp', {
    method: 'POST',
    headers: json,
    body: mcp(2, 'tools/call', { name: toolName, arguments: { userId: SUB, note: BODY_MARKER } }),
  });

  const fresh = lines().slice(lineCountBefore);
  order.forEach((label, i) => {
    const entry = sent[label];
    if (entry) entry.line = fresh[i];
  });
});

afterAll(async () => {
  await stack?.stop();
  if (dir) rmSync(dir, { recursive: true, force: true });
});

describe('LOG_FILE', () => {
  it('should write only JSON lines', () => {
    const raw = readRaw()
      .split('\n')
      .filter(l => l.trim() !== '');
    expect(raw.length).toBeGreaterThan(0);
    for (const l of raw) expect(() => JSON.parse(l)).not.toThrow();
  });

  it('should write exactly one line per request', () => {
    expect(lines().length - lineCountBefore).toBe(order.length);
    const ids = lines().map(l => l.requestId);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('should carry context, route, sub, request id, status and duration on a request line', () => {
    const line = sent.get?.line;
    expect(line).toBeDefined();
    expect(line?.context).toBe('kernel');
    expect(line?.method).toBe('GET');
    expect(line?.route).toBe('/health');
    expect(line?.sub).toBe(SUB);
    expect(typeof line?.requestId).toBe('string');
    expect(line?.requestId).not.toBe('');
    expect(line?.status).toBe(200);
    expect(line?.level).toBe('info');
    expect(typeof line?.durationMs).toBe('number');
    expect(Number.isNaN(Date.parse(line?.time as string))).toBe(false);
  });

  it('should log an error request at error level with its problem type', () => {
    const anon = sent.anon;
    expect(anon?.status).toBe(401);
    expect(anon?.line?.status).toBe(401);
    expect(anon?.line?.level).toBe('error');
    expect(anon?.line?.problem).toBe('/problems/v1/unauthenticated');
    expect(anon?.line?.sub ?? null).toBeNull();
    // The line's request id is the traceId the caller got in the problem body.
    expect((JSON.parse(anon?.body ?? '{}') as { traceId?: string }).traceId).toBe(anon?.line?.requestId);

    const missing = sent.missing;
    expect(missing?.line?.status).toBe(missing?.status as number);
    expect(missing?.status).toBeGreaterThanOrEqual(400);
    expect(missing?.line?.level).toBe('error');
    expect(missing?.line?.problem).toStartWith('/problems/v1/');
    expect(missing?.line?.sub).toBe(SUB);
  });

  it('should log an MCP tools/call with its tool name', () => {
    const line = sent.mcp?.line;
    expect(line).toBeDefined();
    expect(line?.tool).toBe(toolName);
    expect(toolName).not.toBe('logfile.unknown'); // the app must list at least one tool
    expect(line?.context).toBe(toolName.split('.')[0] as string);
    expect(line?.sub).toBe(SUB);
    expect(typeof line?.status).toBe('number');
    expect(typeof line?.durationMs).toBe('number');
  });

  it('should not contain a token, an email or a request body anywhere', () => {
    const raw = readRaw();
    const payload = token.split('.')[1] ?? '';
    expect(payload.length).toBeGreaterThan(10);
    expect(raw).not.toContain(token);
    expect(raw).not.toContain(payload);
    expect(raw).not.toMatch(/eyJ[A-Za-z0-9_-]{5,}\./);
    expect(raw.toLowerCase()).not.toContain('bearer ey');
    expect(raw).not.toContain(EMAIL);
    expect(raw).not.toMatch(/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/);
    expect(raw).not.toContain(BODY_MARKER);
  });
});
