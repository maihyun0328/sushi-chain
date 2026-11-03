// ai-chart-server 계약의 로컬 mock (기준: ai-chart-server/app, 클라이언트 문서: Docs/custom-song-api.md)
// 실행: node Tools/mock-song-server/server.ts   (Node 23.6+ type stripping, 의존성 없음)
// 실제 서버와 달리 JWT 서명/scope는 검증하지 않는다. Bearer 헤더가 비어 있지 않은지만 본다.
import { createServer } from "node:http";
import type { IncomingMessage, ServerResponse } from "node:http";
import { createReadStream, existsSync, readFileSync, statSync } from "node:fs";
import { createHash, randomUUID } from "node:crypto";
import { resolve } from "node:path";

const PORT = Number(process.env.PORT ?? 8000);
const PREFIX = "/api/v1";
const PUBLIC_BASE = `http://localhost:${PORT}${PREFIX}`; // 실제 서버처럼 asset URL은 절대 URL
const REPO_ROOT = resolve(import.meta.dirname, "..", "..");

const QUEUED_MS = 1000; // queued 유지 시간
const PROCESS_MS = 5000; // processing 유지 시간 → 이후 completed
const FAIL_AFTER_MS = 3000; // 실패 시나리오는 이 시점에 failed
const ALGO_VERSION = "1.0.0-mock";
const NO_STORE = { "Cache-Control": "no-store" };

const AUDIO = {
  path: resolve(REPO_ROOT, "Assets/Package/BewhY (비와이) - SOUTHSIDE FREESTYLE (Official Video).mp3"),
  type: "audio/mpeg",
};
// 저장소에 있는 유일한 jpg (DOTween 에디터 헤더 이미지, 512x164). 실제 서버와 같은 image/jpeg로 보내기 위해 쓴다.
const THUMB = {
  path: resolve(REPO_ROOT, "Assets/Plugins/Demigiant/DOTween/Editor/Imgs/Header.jpg"),
  type: "image/jpeg",
};
const CHART_PATH = resolve(REPO_ROOT, "Assets/Resources/SOUTHSIDE FREESTYLE.json");
const CHART: unknown = JSON.parse(readFileSync(CHART_PATH, "utf8").replace(/^﻿/, ""));
const SONG_DURATION = 173.479; // SOUTHSIDE mp3 길이(초)
const SONG_BPM = 95.0;

type Difficulty = "easy" | "normal" | "hard";
type JobState = "queued" | "processing" | "completed" | "failed";
const DIFFICULTIES = new Set<string>(["easy", "normal", "hard"]);

interface Job {
  jobId: string;
  youtubeId: string;
  difficulty: Difficulty;
  createdAt: number;
  cached: boolean; // 캐시 히트로 바로 completed
  failCode: "analysis_failed" | "too_long" | null;
}

const jobs = new Map<string, Job>();
let busyServed = false; // "busy"가 든 첫 요청만 503

const ERROR_MESSAGES = {
  analysis_failed: "Audio analysis failed. (mock)",
  too_long: "The song is longer than the 300 second limit. (mock)",
};

// ===== YouTube URL 파싱 (app/services/youtube_url.py 와 같은 규칙) =====
const VIDEO_ID = /^[A-Za-z0-9_-]{11}$/;
const YT_HOSTS = new Set(["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com"]);
const ALLOWED_CHARS = /^[\x21-\x5b\x5d-\x7e]+$/;

export function extractVideoId(raw: string): string | null {
  if (!raw || raw.length > 200 || !ALLOWED_CHARS.test(raw)) return null;
  const m = raw.match(/^([A-Za-z][A-Za-z0-9+.-]*):\/\/([^/?#]*)([^?#]*)(\?[^#]*)?/);
  if (!m) return null;
  const scheme = m[1].toLowerCase();
  const netloc = m[2];
  if (scheme !== "http" && scheme !== "https") return null;
  if (!netloc || netloc.includes("@") || netloc.includes(":")) return null;
  const host = netloc.toLowerCase();
  const path = m[3];
  const query = new URLSearchParams((m[4] ?? "").slice(1));
  let id: string | null = null;
  if (host === "youtu.be") {
    let segs = path.split("/").slice(1);
    if (segs.length === 2 && segs[1] === "") segs = segs.slice(0, 1);
    id = segs.length === 1 ? segs[0] : null;
  } else if (YT_HOSTS.has(host)) {
    if (path === "/watch" || path === "/watch/") {
      const vs = query.getAll("v");
      id = vs.length > 0 && new Set(vs).size === 1 ? vs[0] : null;
    } else {
      let segs = path.split("/").slice(1);
      if (segs.length > 0 && segs[segs.length - 1] === "") segs = segs.slice(0, -1);
      id = segs.length === 2 && ["shorts", "embed", "live"].includes(segs[0]) ? segs[1] : null;
    }
  } else {
    return null;
  }
  return id && VIDEO_ID.test(id) ? id : null;
}

// assetId: youtubeId 단위로 고정 (난이도 공유), 32자리 소문자 hex
function assetIdOf(youtubeId: string): string {
  return createHash("sha256").update(`youtube:${youtubeId}`).digest("hex").slice(0, 32);
}

function stateOf(job: Job): JobState {
  if (job.cached) return "completed";
  const elapsed = Date.now() - job.createdAt;
  if (elapsed < QUEUED_MS) return "queued";
  if (job.failCode) return elapsed < FAIL_AFTER_MS ? "processing" : "failed";
  return elapsed < QUEUED_MS + PROCESS_MS ? "processing" : "completed";
}

// 서버 캐시 흉내: 완료된 job이 있으면 그 채보/asset이 존재한다
function hasCompleted(pred: (j: Job) => boolean): boolean {
  for (const j of jobs.values()) if (pred(j) && stateOf(j) === "completed") return true;
  return false;
}

function viewJob(job: Job) {
  const status = stateOf(job);
  const base = { jobId: job.jobId, status, difficulty: job.difficulty };
  // 실제 서버(FastAPI response_model)처럼 없는 필드는 null로 내려준다
  const empty = { song: null, chart: null, error: null, algorithmVersion: null };
  if (status === "failed") {
    const code = job.failCode ?? "analysis_failed";
    return { ...base, ...empty, error: { code, message: ERROR_MESSAGES[code] } };
  }
  if (status !== "completed") return { ...base, ...empty };

  const assetId = assetIdOf(job.youtubeId);
  return {
    ...base,
    song: {
      youtubeId: job.youtubeId,
      title: `Mock Song ${job.youtubeId}`,
      artist: null,
      duration: SONG_DURATION,
      bpm: SONG_BPM,
      thumbnailUrl: `${PUBLIC_BASE}/assets/${assetId}/thumbnail`,
      audioUrl: `${PUBLIC_BASE}/assets/${assetId}/audio`,
    },
    chart: CHART,
    error: null,
    algorithmVersion: ALGO_VERSION,
  };
}

// ===== HTTP helpers =====
function sendJson(res: ServerResponse, code: number, body: unknown, headers: Record<string, string> = {}): void {
  const data = JSON.stringify(body);
  res.writeHead(code, {
    "Content-Type": "application/json",
    "Content-Length": Buffer.byteLength(data),
    ...headers,
  });
  res.end(data);
}

async function readBody(req: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const c of req) {
    size += (c as Buffer).length;
    if (size > 64 * 1024) throw new Error("body too large");
    chunks.push(c as Buffer);
  }
  return Buffer.concat(chunks).toString("utf8");
}

function invalidRequest(res: ServerResponse, errors: Array<{ loc: string[]; msg: string; type: string }>): void {
  sendJson(res, 422, { detail: "invalid_request", errors }, NO_STORE);
}

function sendFile(req: IncomingMessage, res: ServerResponse, path: string, type: string, rangeOk: boolean): void {
  const size = statSync(path).size;
  const headers: Record<string, string | number> = { "Content-Type": type, "Cache-Control": "private, max-age=86400" };
  if (rangeOk) headers["Accept-Ranges"] = "bytes";
  const range = rangeOk ? req.headers.range : undefined;
  const m = range?.match(/^bytes=(\d*)-(\d*)$/);
  if (m && (m[1] !== "" || m[2] !== "")) {
    let start: number;
    let end: number;
    if (m[1] === "") {
      start = Math.max(0, size - Number(m[2]));
      end = size - 1;
    } else {
      start = Number(m[1]);
      end = m[2] === "" ? size - 1 : Math.min(Number(m[2]), size - 1);
    }
    if (start >= size || start > end) {
      res.writeHead(416, { "Content-Range": `bytes */${size}` });
      return void res.end();
    }
    res.writeHead(206, { ...headers, "Content-Range": `bytes ${start}-${end}/${size}`, "Content-Length": end - start + 1 });
    if (req.method === "HEAD") return void res.end();
    createReadStream(path, { start, end }).pipe(res);
    return;
  }
  res.writeHead(200, { ...headers, "Content-Length": size });
  if (req.method === "HEAD") return void res.end();
  createReadStream(path).pipe(res);
}

// ===== routes =====
async function handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
  const url = new URL(req.url ?? "/", "http://localhost");
  const method = req.method ?? "GET";
  const path = url.pathname;

  if (path === "/health" && method === "GET") return sendJson(res, 200, { status: "ok" });
  if (path !== PREFIX && !path.startsWith(PREFIX + "/")) return sendJson(res, 404, { detail: "Not Found" });

  // prefix 아래 모든 경로(asset, 없는 경로 포함)는 Bearer 필수. 실제 서버도 401이 아니라 403.
  const auth = req.headers.authorization ?? "";
  if (!/^Bearer\s+\S+/.test(auth)) return sendJson(res, 403, { detail: "forbidden" }, NO_STORE);

  const route = path.slice(PREFIX.length);

  if (route === "/charts" && method === "POST") {
    let body: Record<string, unknown>;
    try {
      const parsed = JSON.parse(await readBody(req));
      if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) throw new Error("not an object");
      body = parsed as Record<string, unknown>;
    } catch {
      return invalidRequest(res, [{ loc: ["body"], msg: "JSON decode error", type: "json_invalid" }]);
    }
    const errors: Array<{ loc: string[]; msg: string; type: string }> = [];
    const youtubeUrl = body.youtubeUrl;
    if (typeof youtubeUrl !== "string") errors.push({ loc: ["body", "youtubeUrl"], msg: "Field required", type: "missing" });
    else if (youtubeUrl.length > 200) errors.push({ loc: ["body", "youtubeUrl"], msg: "String should have at most 200 characters", type: "string_too_long" });
    const difficulty = body.difficulty ?? "normal";
    if (typeof difficulty !== "string" || !DIFFICULTIES.has(difficulty)) {
      errors.push({ loc: ["body", "difficulty"], msg: "Input should be 'easy', 'normal' or 'hard'", type: "enum" });
    }
    for (const k of Object.keys(body)) {
      if (k !== "youtubeUrl" && k !== "difficulty") errors.push({ loc: ["body", k], msg: "Extra inputs are not permitted", type: "extra_forbidden" });
    }
    if (errors.length > 0) return invalidRequest(res, errors);

    const urlStr = youtubeUrl as string;
    const youtubeId = extractVideoId(urlStr);
    if (!youtubeId) return sendJson(res, 422, { detail: "invalid_youtube_url" }, NO_STORE);
    const diff = difficulty as Difficulty;

    // 1) 캐시: 같은 youtubeId+difficulty가 이미 완료됐으면 새 jobId로 바로 completed
    if (hasCompleted((j) => j.youtubeId === youtubeId && j.difficulty === diff)) {
      const job: Job = { jobId: randomUUID(), youtubeId, difficulty: diff, createdAt: Date.now(), cached: true, failCode: null };
      jobs.set(job.jobId, job);
      return sendAccepted(res, job);
    }
    // 2) 진행 중인 같은 요청이 있으면 그 jobId를 그대로 돌려준다
    for (const j of jobs.values()) {
      if (j.youtubeId === youtubeId && j.difficulty === diff) {
        const s = stateOf(j);
        if (s === "queued" || s === "processing") return sendAccepted(res, j);
      }
    }
    // 3) "busy"가 든 첫 요청만 큐가 가득 찬 것처럼 503
    if (urlStr.includes("busy") && !busyServed) {
      busyServed = true;
      return sendJson(res, 503, { detail: "queue_full" }, { ...NO_STORE, "Retry-After": "2" });
    }
    const failCode = urlStr.includes("toolong") ? "too_long" : urlStr.includes("fail") ? "analysis_failed" : null;
    const job: Job = { jobId: randomUUID(), youtubeId, difficulty: diff, createdAt: Date.now(), cached: false, failCode };
    jobs.set(job.jobId, job);
    return sendAccepted(res, job);
  }

  const jobMatch = route.match(/^\/charts\/([^/]+)$/);
  if (jobMatch && method === "GET") {
    const job = jobs.get(decodeURIComponent(jobMatch[1]).toLowerCase());
    if (!job) return sendJson(res, 404, { detail: "not_found" }, NO_STORE);
    return sendJson(res, 200, viewJob(job), NO_STORE);
  }

  const assetMatch = route.match(/^\/assets\/([^/]+)\/(audio|thumbnail)$/);
  if (assetMatch && (method === "GET" || method === "HEAD")) {
    const [, assetId, kind] = assetMatch;
    if (!/^[0-9a-f]{32}$/.test(assetId) || !hasCompleted((j) => assetIdOf(j.youtubeId) === assetId)) return sendJson(res, 404, { detail: "not_found" }, NO_STORE);
    const f = kind === "audio" ? AUDIO : THUMB;
    if (!existsSync(f.path)) return sendJson(res, 404, { detail: "not_found" }, NO_STORE);
    return sendFile(req, res, f.path, f.type, kind === "audio");
  }

  sendJson(res, 404, { detail: "Not Found" });
}

function sendAccepted(res: ServerResponse, job: Job): void {
  sendJson(res, 202, { jobId: job.jobId, status: stateOf(job) }, { ...NO_STORE, Location: `${PREFIX}/charts/${job.jobId}` });
}

const server = createServer((req, res) => {
  const start = Date.now();
  res.on("finish", () => {
    console.log(`${new Date().toISOString()} ${req.method} ${req.url} -> ${res.statusCode} (${Date.now() - start}ms)`);
  });
  handle(req, res).catch((err: unknown) => {
    console.error(err);
    if (!res.headersSent) sendJson(res, 500, { detail: "internal_error" });
    else res.destroy();
  });
});

server.listen(PORT, () => {
  console.log(`mock ai-chart-server listening on ${PUBLIC_BASE}`);
  for (const [name, p] of [["audio", AUDIO.path], ["thumbnail", THUMB.path], ["chart", CHART_PATH]]) {
    console.log(`  ${name} <- ${p}${existsSync(p) ? "" : "  (MISSING)"}`);
  }
});
