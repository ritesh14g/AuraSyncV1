// AuraSync AI server - a Cloudflare Worker.
//
// Why it exists: the app needs an AI for part chat, search and quizzes, and an AI key shipped
// inside an APK can be pulled out by anyone. So the keys live here, as Worker secrets, and the
// app talks only to this Worker.
//
//   GET  /health   which providers are configured (no key needed)
//   POST /ask      a question about a part, or the whole model     -> {answer, suggestions[]}
//   POST /find     which part does "the part that burns fuel" mean -> {matches[{path, why}]}
//   POST /quiz     multiple-choice questions from the part facts   -> {questions[...]}
//   POST /transcribe  a short WAV recording (voice search) to text   -> {text}
//
// Every POST needs the header  X-AuraSync-Key: <APP_KEY>. That key is inside the app, so it is
// a deterrent rather than real security - but the AI providers are on free tiers, so the worst
// anyone can do with it is use up the day's free quota, never run up a bill.
//
// Secrets (npx wrangler secret put NAME): GEMINI_API_KEY, GROQ_API_KEY, APP_KEY.
// Plain settings (wrangler.toml [vars]): GEMINI_CHAT_MODEL, GEMINI_QUIZ_MODEL, GROQ_MODEL - each a
// comma-separated list of models, tried in order (providers.js explains why).

import {
  callModel, transcribe, ProviderError, modelList,
  DEFAULT_GEMINI_CHAT, DEFAULT_GEMINI_QUIZ, DEFAULT_GROQ, DEFAULT_GROQ_STT,
} from "./providers.js";
import {
  ASK_SYSTEM, FIND_SYSTEM, QUIZ_SYSTEM,
  factsBlock, findUserMessage, quizUserMessage,
} from "./prompts.js";

const MAX_BODY_BYTES = 64 * 1024;
const MAX_AUDIO_BYTES = 1024 * 1024; // about 30 s of 16 kHz mono; voice search sends about 5 s
const RATE_LIMIT_PER_MINUTE = 30;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const route = `${request.method} ${url.pathname.replace(/\/+$/, "") || "/"}`;

    try {
      if (route === "GET /health" || route === "GET /") return health(env);

      if (!["POST /ask", "POST /find", "POST /quiz", "POST /transcribe"].includes(route)) {
        return fail(404, "not_found", `No route ${route}`);
      }

      if (!env.APP_KEY) return fail(503, "not_configured", "APP_KEY secret is not set");
      if (request.headers.get("x-aurasync-key") !== env.APP_KEY) {
        return fail(401, "bad_key", "Missing or wrong X-AuraSync-Key header");
      }
      if (!env.GEMINI_API_KEY && !env.GROQ_API_KEY) {
        return fail(503, "not_configured", "No AI provider key is set");
      }

      const client = request.headers.get("cf-connecting-ip") || "local";
      if (!allow(client)) {
        return fail(429, "rate_limited", `More than ${RATE_LIMIT_PER_MINUTE} requests a minute; wait a moment`);
      }

      if (route === "POST /transcribe") return await speechToText(env, request);

      const body = await readJson(request);

      if (route === "POST /ask") return await ask(env, body);
      if (route === "POST /find") return await find(env, body);
      return await quiz(env, body);
    } catch (err) {
      if (err instanceof BadRequest) return fail(400, "bad_request", err.message);
      if (err instanceof ProviderError) {
        console.log(`AI failed: ${err.message}`);
        return fail(502, "ai_unavailable", "The AI could not answer right now. Please try again later.", err.message);
      }
      console.log(`Unexpected error: ${err && err.stack || err}`);
      return fail(500, "internal", "Something went wrong on the server");
    }
  },
};

// ── Routes ──────────────────────────────────────────────────────────────────

function health(env) {
  return ok({
    service: "aurasync-ai",
    providers: {
      gemini: Boolean(env.GEMINI_API_KEY),
      groq: Boolean(env.GROQ_API_KEY),
    },
    models: {
      chat: modelList(env.GEMINI_CHAT_MODEL, DEFAULT_GEMINI_CHAT),
      quiz: modelList(env.GEMINI_QUIZ_MODEL, DEFAULT_GEMINI_QUIZ),
      fallback: modelList(env.GROQ_MODEL, DEFAULT_GROQ),
      speech: modelList(env.GROQ_STT_MODEL, DEFAULT_GROQ_STT),
    },
    appKeySet: Boolean(env.APP_KEY),
  });
}

/**
 * Body: { model: {name, context, summary}, part?: {label, function, material, connectsTo[]},
 *         history?: [{role: "user"|"assistant", text}], question }
 */
async function ask(env, body) {
  const model = cleanModel(body.model);
  // Unity's JsonUtility always writes every field, so "no part" arrives as an empty object.
  const cleaned = body.part ? cleanPart(body.part) : null;
  const part = cleaned && (cleaned.label || cleaned.function) ? cleaned : null;
  const question = str(body.question, 500);
  if (!question) throw new BadRequest("question is required");

  const history = arr(body.history, 8)
    .map((t) => ({ role: t && t.role === "assistant" ? "assistant" : "user", text: str(t && t.text, 1000) }))
    .filter((t) => t.text);

  // The facts ride on the first user turn only, so a long conversation does not repeat them.
  const all = [...history, { role: "user", text: question }];
  const first = all.findIndex((t) => t.role === "user");
  all[first] = { role: "user", text: `${factsBlock(model, part)}\n\nQuestion: ${all[first].text}` };

  // Providers require turns to alternate, starting with the user.
  const turns = alternate(all);

  const r = await callModel(env, {
    tier: "chat", system: ASK_SYSTEM, messages: turns, json: true, maxTokens: 4096, temperature: 0.5,
    validate: (d) => {
      const answer = str(d.answer, 2000);
      if (!answer) return null;
      return { answer, suggestions: arr(d.suggestions, 3).map((s) => str(s, 80)).filter(Boolean) };
    },
  });

  return ok({ ...r.data, ...meta(r) });
}

/** Body: { query, parts: [{path, label, aliases[], function}] } */
async function find(env, body) {
  const query = str(body.query, 200);
  if (!query) throw new BadRequest("query is required");

  const parts = arr(body.parts, 400).map((p) => ({
    path: str(p && p.path, 64),
    label: str(p && p.label, 120),
    aliases: arr(p && p.aliases, 10).map((a) => str(a, 60)).filter(Boolean),
    function: str(p && p.function, 160),
  })).filter((p) => p.path);
  if (parts.length === 0) throw new BadRequest("parts list is empty");

  const r = await callModel(env, {
    tier: "chat", system: FIND_SYSTEM, json: true, maxTokens: 2048, temperature: 0.1,
    messages: [{ role: "user", text: findUserMessage(query, parts) }],
  });

  // Never pass on a path the app did not send: the model can invent one.
  const known = new Set(parts.map((p) => p.path));
  const seen = new Set();
  const matches = arr(r.data.matches, 5)
    .map((m) => ({ path: str(m && m.path, 64), why: str(m && m.why, 120) }))
    .filter((m) => known.has(m.path) && !seen.has(m.path) && seen.add(m.path));

  return ok({ matches, ...meta(r) });
}

/** Body: { model: {name, context, summary}, parts: [...], count, difficulty } */
async function quiz(env, body) {
  const model = cleanModel(body.model);
  const parts = arr(body.parts, 60).map((raw) => ({ ...cleanPart(raw), path: str(raw && raw.path, 64) }));
  const count = Math.min(15, Math.max(1, Number.parseInt(body.count, 10) || 5));
  const difficulty = ["easy", "medium", "hard"].includes(body.difficulty) ? body.difficulty : "medium";

  const known = new Set(parts.map((p) => p.path));
  const r = await callModel(env, {
    tier: "quiz", system: QUIZ_SYSTEM, json: true, maxTokens: 16384, temperature: 0.7,
    messages: [{ role: "user", text: quizUserMessage(model, parts, count, difficulty) }],
    validate: (d) => {
      const questions = arr(d.questions, count)
        .map((q) => ({
          question: str(q && q.question, 300),
          options: arr(q && q.options, 4).map((o) => str(o, 160)),
          answer: Number.isInteger(q && q.answer) ? q.answer : -1,
          partPath: known.has(str(q && q.partPath, 64)) ? str(q.partPath, 64) : "",
          explanation: str(q && q.explanation, 300),
        }))
        // A question the app cannot mark is worse than a missing one.
        .filter((q) => q.question && q.options.length === 4 && q.options.every(Boolean) && q.answer >= 0 && q.answer < 4);
      return questions.length ? { questions } : null;
    },
  });

  return ok({ ...r.data, requested: count, ...meta(r) });
}

/**
 * Body: the raw bytes of a WAV file (Content-Type audio/wav).
 * Header X-AuraSync-Vocabulary (optional, URI-encoded): words to expect, such as the part names.
 */
async function speechToText(env, request) {
  const declared = Number(request.headers.get("content-length") || 0);
  if (declared > MAX_AUDIO_BYTES) throw new BadRequest("recording too long");

  const bytes = new Uint8Array(await request.arrayBuffer());
  if (bytes.length > MAX_AUDIO_BYTES) throw new BadRequest("recording too long");
  if (bytes.length < 44 || String.fromCharCode(...bytes.subarray(0, 4)) !== "RIFF") {
    throw new BadRequest("body must be a WAV file");
  }

  let hint = "";
  try { hint = decodeURIComponent(request.headers.get("x-aurasync-vocabulary") || "").slice(0, 600); }
  catch { hint = ""; }

  const r = await transcribe(env, bytes, hint);
  return ok({ text: r.text, provider: r.provider, model: r.model, ...(r.fallbackReason ? { fallbackReason: r.fallbackReason } : {}) });
}

// ── Helpers ─────────────────────────────────────────────────────────────────

class BadRequest extends Error {}

async function readJson(request) {
  const declared = Number(request.headers.get("content-length") || 0);
  if (declared > MAX_BODY_BYTES) throw new BadRequest("request body too large");

  const text = await request.text();
  if (text.length > MAX_BODY_BYTES) throw new BadRequest("request body too large");
  try {
    const value = JSON.parse(text);
    if (value === null || typeof value !== "object" || Array.isArray(value)) throw new Error();
    return value;
  } catch {
    throw new BadRequest("body must be a JSON object");
  }
}

function cleanModel(m) {
  m = m || {};
  return { name: str(m.name, 120), context: str(m.context, 300), summary: str(m.summary, 1500) };
}

function cleanPart(p) {
  p = p || {};
  return {
    label: str(p.label, 120),
    function: str(p.function, 800),
    material: str(p.material, 160),
    connectsTo: arr(p.connectsTo, 12).map((c) => str(c, 120)).filter(Boolean),
  };
}

function str(v, max) {
  return typeof v === "string" ? v.trim().slice(0, max) : "";
}

function arr(v, max) {
  return Array.isArray(v) ? v.slice(0, max) : [];
}

function alternate(turns) {
  const out = [];
  for (const t of turns) {
    const last = out[out.length - 1];
    if (last && last.role === t.role) last.text += `\n\n${t.text}`;
    else out.push({ ...t });
  }
  // Both providers want the conversation to start with the user.
  if (out.length && out[0].role !== "user") out.shift();
  return out;
}

function meta(r) {
  return { provider: r.provider, model: r.model, ...(r.fallbackReason ? { fallbackReason: r.fallbackReason } : {}) };
}

function ok(payload) {
  return json(200, { ok: true, ...payload });
}

function fail(status, error, message, detail) {
  return json(status, { ok: false, error, message, ...(detail ? { detail } : {}) });
}

function json(status, value) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
  });
}

// Best-effort limit per client. A Worker runs as many separate copies around the world and each
// keeps its own count, so this only stops one runaway client hammering one location. The free
// providers' daily quotas are the real ceiling.
const hits = new Map();
function allow(client) {
  const now = Date.now();
  const recent = (hits.get(client) || []).filter((t) => now - t < 60000);
  if (recent.length >= RATE_LIMIT_PER_MINUTE) {
    hits.set(client, recent);
    return false;
  }
  recent.push(now);
  hits.set(client, recent);
  if (hits.size > 5000) hits.clear(); // never let the map grow without bound
  return true;
}

// For tests only.
export function _resetRateLimit() {
  hits.clear();
}
