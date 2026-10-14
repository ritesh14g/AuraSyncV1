// Offline tests: the real Gemini and Groq are replaced by a fake fetch, so these run with no
// keys and no network.  Run with:  npm test

import { test, beforeEach } from "node:test";
import assert from "node:assert/strict";
import worker, { _resetRateLimit } from "../src/index.js";

// One model per provider, so each test controls exactly which calls happen. The model-list
// behaviour has its own tests at the end.
const ENV = {
  APP_KEY: "test-app-key",
  GEMINI_API_KEY: "g-key",
  GROQ_API_KEY: "q-key",
  GEMINI_CHAT_MODEL: "gemini-chat",
  GEMINI_QUIZ_MODEL: "gemini-quiz",
  GROQ_MODEL: "groq-model",
};

let calls;          // every upstream request the Worker made, in order
let script;         // what each provider should answer, set per test

beforeEach(() => {
  calls = [];
  script = { gemini: [], groq: [] };
  _resetRateLimit();

  globalThis.fetch = async (url, init) => {
    const provider = String(url).includes("googleapis") ? "gemini" : "groq";
    const body = typeof init.body === "string" ? JSON.parse(init.body) : init.body; // FormData for speech
    calls.push({ provider, url: String(url), body, headers: init.headers });
    const next = script[provider].shift();
    if (!next) throw new Error(`unexpected ${provider} call`);
    if (next.throw) throw Object.assign(new Error(next.throw), { name: next.name || "Error" });
    return new Response(typeof next.body === "string" ? next.body : JSON.stringify(next.body), {
      status: next.status || 200,
    });
  };
});

const geminiSays = (obj) => ({ body: { candidates: [{ content: { parts: [{ text: JSON.stringify(obj) }] } }] } });
const groqSays = (obj) => ({ body: { choices: [{ message: { content: JSON.stringify(obj) } }] } });

function post(path, body, env = ENV, key = ENV.APP_KEY) {
  const headers = { "content-type": "application/json" };
  if (key) headers["x-aurasync-key"] = key;
  return worker.fetch(new Request(`https://w.test${path}`, {
    method: "POST", headers, body: typeof body === "string" ? body : JSON.stringify(body),
  }), env);
}

const ASK = {
  model: { name: "Jet Engine", context: "Turbofan jet engine" },
  part: { label: "Fan", function: "Draws air into the engine", material: "Titanium" },
  question: "Why titanium?",
};

// ── Health and access ───────────────────────────────────────────────────────

test("health needs no key and reports configuration without revealing secrets", async () => {
  const res = await worker.fetch(new Request("https://w.test/health"), ENV);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.providers.gemini, true);
  assert.equal(body.providers.groq, true);
  assert.ok(!JSON.stringify(body).includes("g-key"), "secret leaked");
});

test("missing or wrong app key is refused before any AI call", async () => {
  assert.equal((await post("/ask", ASK, ENV, null)).status, 401);
  assert.equal((await post("/ask", ASK, ENV, "wrong")).status, 401);
  assert.equal(calls.length, 0);
});

test("server with no APP_KEY set refuses everything", async () => {
  const res = await post("/ask", ASK, { ...ENV, APP_KEY: undefined });
  assert.equal(res.status, 503);
});

test("unknown route is 404", async () => {
  assert.equal((await post("/nope", {})).status, 404);
});

test("bad JSON body is 400", async () => {
  assert.equal((await post("/ask", "{not json")).status, 400);
  assert.equal((await post("/ask", "[1,2]")).status, 400);
});

test("oversized body is refused", async () => {
  const res = await post("/ask", { ...ASK, question: "x".repeat(70 * 1024) });
  assert.equal(res.status, 400);
});

test("rate limit kicks in after 30 requests a minute from one client", async () => {
  for (let i = 0; i < 30; i++) script.gemini.push(geminiSays({ answer: "a", suggestions: [] }));
  for (let i = 0; i < 30; i++) assert.equal((await post("/ask", ASK)).status, 200);
  assert.equal((await post("/ask", ASK)).status, 429);
});

// ── /ask ────────────────────────────────────────────────────────────────────

test("ask: Gemini answers; facts reach the model; JSON mode requested", async () => {
  script.gemini.push(geminiSays({ answer: "Light and strong.", suggestions: ["What else is titanium used for?"] }));
  const res = await post("/ask", ASK);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.answer, "Light and strong.");
  assert.deepEqual(body.suggestions, ["What else is titanium used for?"]);
  assert.equal(body.provider, "gemini");

  const sent = calls[0].body;
  assert.equal(calls[0].headers["x-goog-api-key"], "g-key");
  assert.match(calls[0].url, /\/gemini-chat:generateContent$/);
  assert.equal(sent.generationConfig.responseMimeType, "application/json");
  assert.deepEqual(sent.generationConfig.thinkingConfig, { thinkingLevel: "minimal" }, "chat should barely think");
  assert.match(sent.contents[0].parts[0].text, /Selected part: Fan/);
  assert.match(sent.contents[0].parts[0].text, /Why titanium\?/);
});

test("ask: Gemini quota exhausted (429) falls back to Groq", async () => {
  script.gemini.push({ status: 429, body: { error: { status: "RESOURCE_EXHAUSTED" } } });
  script.groq.push(groqSays({ answer: "From Groq.", suggestions: [] }));
  const body = await (await post("/ask", ASK)).json();
  assert.equal(body.provider, "groq");
  assert.equal(body.answer, "From Groq.");
  assert.match(body.fallbackReason, /429/);
  assert.equal(calls[1].headers.authorization, "Bearer q-key");
  assert.deepEqual(calls[1].body.response_format, { type: "json_object" });
});

test("ask: Gemini timeout, outage, or non-JSON answer all fall back", async () => {
  for (const failure of [
    { throw: "slow", name: "TimeoutError" },
    { status: 503, body: "down" },
    { body: { candidates: [{ content: { parts: [{ text: "sorry, plain text" }] } }] } },
    { body: { candidates: [] , promptFeedback: { blockReason: "SAFETY" } } },
  ]) {
    script.gemini.push(failure);
    script.groq.push(groqSays({ answer: "ok", suggestions: [] }));
    const res = await post("/ask", ASK);
    assert.equal(res.status, 200);
    assert.equal((await res.json()).provider, "groq");
  }
});

test("ask: both providers failing gives a friendly 502", async () => {
  script.gemini.push({ status: 429, body: "quota" });
  script.groq.push({ status: 500, body: "boom" });
  const res = await post("/ask", ASK);
  assert.equal(res.status, 502);
  const body = await res.json();
  assert.equal(body.error, "ai_unavailable");
  assert.match(body.message, /try again/i);
});

test("ask: works with only one provider configured", async () => {
  script.groq.push(groqSays({ answer: "only groq", suggestions: [] }));
  const res = await post("/ask", ASK, { ...ENV, GEMINI_API_KEY: undefined });
  assert.equal((await res.json()).answer, "only groq");
  assert.equal(calls.length, 1);
});

test("ask: a ```json fenced answer is still accepted", async () => {
  script.gemini.push({ body: { candidates: [{ content: { parts: [{ text: "```json\n{\"answer\":\"x\",\"suggestions\":[]}\n```" }] } }] } });
  assert.equal((await (await post("/ask", ASK)).json()).answer, "x");
});

test("ask: history keeps facts on the first turn only and turns alternate", async () => {
  script.gemini.push(geminiSays({ answer: "a", suggestions: [] }));
  await post("/ask", {
    ...ASK,
    history: [
      { role: "user", text: "What is it?" },
      { role: "assistant", text: "A fan." },
    ],
    question: "And the material?",
  });
  const contents = calls[0].body.contents;
  assert.deepEqual(contents.map((c) => c.role), ["user", "model", "user"]);
  assert.match(contents[0].parts[0].text, /Selected part: Fan[\s\S]*What is it\?/);
  assert.equal(contents[2].parts[0].text, "And the material?");
});

test("ask: whole-model question (no part) is allowed", async () => {
  script.gemini.push(geminiSays({ answer: "a", suggestions: [] }));
  const res = await post("/ask", { model: ASK.model, question: "What is this?" });
  assert.equal(res.status, 200);
  assert.match(calls[0].body.contents[0].parts[0].text, /whole model/);
});

test("ask: missing question is 400", async () => {
  assert.equal((await post("/ask", { model: ASK.model })).status, 400);
});

// ── /find ───────────────────────────────────────────────────────────────────

const PARTS = [
  { path: "0/0/4/0", label: "Fan", aliases: ["intake fan"], function: "Draws air in" },
  { path: "0/0/5", label: "Combustor", function: "Burns fuel" },
];

test("find: invented paths and duplicates are dropped", async () => {
  script.gemini.push(geminiSays({
    matches: [
      { path: "0/0/5", why: "burns fuel" },
      { path: "9/9/9", why: "made up" },
      { path: "0/0/5", why: "duplicate" },
    ],
  }));
  const body = await (await post("/find", { query: "the part that burns fuel", parts: PARTS })).json();
  assert.deepEqual(body.matches, [{ path: "0/0/5", why: "burns fuel" }]);
  assert.match(calls[0].body.contents[0].parts[0].text, /0\/0\/4\/0: Fan \| also called intake fan/);
});

test("find: empty parts list is 400", async () => {
  assert.equal((await post("/find", { query: "fan", parts: [] })).status, 400);
});

// ── /quiz ───────────────────────────────────────────────────────────────────

test("quiz: uses the quiz model and drops questions the app could not mark", async () => {
  script.gemini.push(geminiSays({
    questions: [
      { question: "What does the fan do?", options: ["a", "b", "c", "d"], answer: 2, partPath: "0/0/4/0", explanation: "e" },
      { question: "Three options only", options: ["a", "b", "c"], answer: 0 },
      { question: "Answer out of range", options: ["a", "b", "c", "d"], answer: 7 },
      { question: "Unknown part path", options: ["a", "b", "c", "d"], answer: 1, partPath: "9/9" },
    ],
  }));
  const res = await post("/quiz", { model: ASK.model, parts: [...PARTS, null], count: 4, difficulty: "hard" });
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.match(calls[0].url, /\/gemini-quiz:generateContent$/);
  assert.equal(body.questions.length, 2);
  assert.equal(body.questions[0].partPath, "0/0/4/0");
  assert.equal(body.questions[1].partPath, "", "unknown path should be blanked, not trusted");
  assert.match(calls[0].body.contents[0].parts[0].text, /Write 4 questions\. Difficulty: hard\./);
});

test("quiz: valid JSON with no usable questions still falls back to Groq", async () => {
  script.gemini.push(geminiSays({ questions: [{ question: "bad", options: [] }] }));
  script.groq.push(groqSays({ questions: [{ question: "q", options: ["a", "b", "c", "d"], answer: 0 }] }));
  const res = await post("/quiz", { model: ASK.model, parts: PARTS, count: 1 });
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.provider, "groq");
  assert.equal(body.questions.length, 1);
  assert.match(body.fallbackReason, /nothing usable/);
});

test("ask: an answer with an empty 'answer' field falls back", async () => {
  script.gemini.push(geminiSays({ answer: "", suggestions: [] }));
  script.groq.push(groqSays({ answer: "real answer", suggestions: [] }));
  assert.equal((await (await post("/ask", ASK)).json()).answer, "real answer");
});

test("quiz: count is clamped to 1..15", async () => {
  script.gemini.push(geminiSays({ questions: [{ question: "q", options: ["a", "b", "c", "d"], answer: 0 }] }));
  await post("/quiz", { model: ASK.model, parts: PARTS, count: 999 });
  assert.match(calls[0].body.contents[0].parts[0].text, /Write 15 questions/);
});

// ── Model lists ─────────────────────────────────────────────────────────────

test("models: a retired Gemini model (404) hands over to the next model in the list", async () => {
  script.gemini.push({ status: 404, body: "model is no longer available" });
  script.gemini.push(geminiSays({ answer: "from the second model", suggestions: [] }));
  const res = await post("/ask", ASK, { ...ENV, GEMINI_CHAT_MODEL: "old-model, new-model" });
  const body = await res.json();
  assert.equal(body.answer, "from the second model");
  assert.equal(body.provider, "gemini");
  assert.equal(body.model, "new-model");
  assert.match(calls[0].url, /\/old-model:generateContent$/);
  assert.match(calls[1].url, /\/new-model:generateContent$/);
  assert.match(body.fallbackReason, /gemini\/old-model: HTTP 404/);
});

test("models: every Gemini model fails, then Groq's list is tried in order", async () => {
  script.gemini.push({ status: 429, body: "quota" }, { status: 429, body: "quota" });
  script.groq.push({ status: 404, body: "no access" });
  script.groq.push(groqSays({ answer: "groq second model", suggestions: [] }));
  const body = await (await post("/ask", ASK, {
    ...ENV, GEMINI_CHAT_MODEL: "g1,g2", GROQ_MODEL: "q1,q2",
  })).json();
  assert.equal(body.model, "q2");
  assert.deepEqual(calls.map((c) => c.provider), ["gemini", "gemini", "groq", "groq"]);
  assert.equal(calls[2].body.model, "q1");
  assert.equal(calls[3].body.model, "q2");
});

test("models: defaults apply when wrangler.toml sets none, and health lists them", async () => {
  const env = { APP_KEY: "k", GEMINI_API_KEY: "g", GROQ_API_KEY: "q" };
  const body = await (await worker.fetch(new Request("https://w.test/health"), env)).json();
  assert.ok(body.models.chat.length >= 1 && body.models.quiz.length >= 1 && body.models.fallback.length >= 1);
  assert.ok(!body.models.chat.some((m) => m.includes("2.5")), "retired Gemini 2.5 models must not be defaults");
});

test("models: Gemini 'thought' parts are not mistaken for the answer", async () => {
  script.gemini.push({ body: { candidates: [{ content: { parts: [
    { text: "let me think about titanium...", thought: true },
    { text: JSON.stringify({ answer: "the real answer", suggestions: [] }) },
  ] } }] } });
  assert.equal((await (await post("/ask", ASK)).json()).answer, "the real answer");
});

test("thinking: quiz thinks a little, and an empty setting sends none", async () => {
  script.gemini.push(geminiSays({ questions: [{ question: "q", options: ["a", "b", "c", "d"], answer: 0 }] }));
  await post("/quiz", { model: ASK.model, parts: PARTS, count: 1 });
  assert.deepEqual(calls[0].body.generationConfig.thinkingConfig, { thinkingLevel: "low" });

  script.gemini.push(geminiSays({ answer: "a", suggestions: [] }));
  await post("/ask", ASK, { ...ENV, GEMINI_THINKING_CHAT: "" });
  assert.equal(calls[1].body.generationConfig.thinkingConfig, undefined);
});

test("order: PROVIDER_ORDER_CHAT can put Groq first; quiz keeps its own order", async () => {
  script.groq.push(groqSays({ answer: "groq first", suggestions: [] }));
  const env = { ...ENV, PROVIDER_ORDER_CHAT: "groq,gemini" };
  assert.equal((await (await post("/ask", ASK, env)).json()).provider, "groq");
  assert.equal(calls.length, 1);

  script.gemini.push(geminiSays({ questions: [{ question: "q", options: ["a", "b", "c", "d"], answer: 0 }] }));
  assert.equal((await (await post("/quiz", { model: ASK.model, parts: PARTS, count: 1 }, env)).json()).provider, "gemini");
});

test("ask: an empty part object (how Unity sends 'no part') means a whole-model question", async () => {
  script.gemini.push(geminiSays({ answer: "a", suggestions: [] }));
  script.groq.push(groqSays({ answer: "a", suggestions: [] }));
  await post("/ask", { model: ASK.model, part: { label: "", function: "", material: "", connectsTo: [] }, question: "What is this?" });
  const text = calls[0].provider === "gemini" ? calls[0].body.contents[0].parts[0].text : calls[0].body.messages[1].content;
  assert.match(text, /whole model/);
  assert.doesNotMatch(text, /Selected part/);
});

// ── /transcribe (voice search) ──────────────────────────────────────────────

function wav(bytes = 2000) {
  const b = new Uint8Array(bytes);
  b.set([0x52, 0x49, 0x46, 0x46]); // "RIFF"
  return b;
}

function postAudio(body, headers = {}, env = ENV) {
  return worker.fetch(new Request("https://w.test/transcribe", {
    method: "POST",
    headers: { "content-type": "audio/wav", "x-aurasync-key": ENV.APP_KEY, ...headers },
    body,
  }), env);
}

test("transcribe: Groq Whisper gets the audio as a file, with the part names as a hint", async () => {
  script.groq.push({ body: { text: " Show me the turbine. " } });
  const res = await postAudio(wav(), { "x-aurasync-vocabulary": encodeURIComponent("Fan rotor, Turbine rotor") });
  const body = await res.json();
  assert.equal(res.status, 200);
  assert.equal(body.text, "Show me the turbine");
  assert.equal(body.provider, "groq");
  assert.match(calls[0].url, /audio\/transcriptions$/);
  const form = calls[0].body;
  assert.ok(form instanceof FormData);
  assert.equal(form.get("model"), "whisper-large-v3-turbo");
  assert.equal(form.get("language"), "en");
  assert.equal(form.get("prompt"), "Fan rotor, Turbine rotor");
  assert.equal(form.get("file").size, 2000);
});

test("transcribe: Groq failing falls back to Gemini listening to the audio", async () => {
  script.groq.push({ status: 429, body: "quota" }, { status: 429, body: "quota" });
  script.gemini.push({ body: { candidates: [{ content: { parts: [{ text: "fan casing" }] } }] } });
  const body = await (await postAudio(wav())).json();
  assert.equal(body.text, "fan casing");
  assert.equal(body.provider, "gemini");
  const part = calls[2].body.contents[0].parts[1];
  assert.equal(part.inlineData.mimeType, "audio/wav");
  assert.ok(part.inlineData.data.startsWith("UklGR"), "base64 of RIFF");
});

test("transcribe: silence hallucinations come back as empty text", async () => {
  script.groq.push({ body: { text: "Thank you." } });
  assert.equal((await (await postAudio(wav())).json()).text, "");
});

test("transcribe: not a WAV, too big, or no key is refused before any AI call", async () => {
  assert.equal((await postAudio(new Uint8Array(100))).status, 400);
  assert.equal((await postAudio(wav(1024 * 1024 + 10))).status, 400);
  const res = await worker.fetch(new Request("https://w.test/transcribe", { method: "POST", body: wav() }), ENV);
  assert.equal(res.status, 401);
  assert.equal(calls.length, 0);
});
