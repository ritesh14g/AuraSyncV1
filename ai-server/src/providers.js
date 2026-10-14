// The two free AI providers, behind one call.
//
// Gemini (Google AI Studio free tier) is tried first. If it fails for any reason - daily quota
// used up (429), an outage (5xx), a timeout, a blocked or empty answer, or an answer that is not
// the JSON we asked for - the same request goes to Groq. Students only ever see an error if both
// fail.
//
// Each provider takes a comma-separated LIST of models (wrangler.toml [vars]), tried in order.
// Two reasons: providers retire free models with little notice (a retired model answers 404, and
// the next one in the list takes over), and free quotas are per model, so a second model is a
// second day's worth of quota.
//
// Neither provider costs money on its free tier, so a runaway client can only use up the day's
// quota, not run up a bill. That is why the Worker's own rate limit (index.js) can be simple.

const GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models";
const GROQ_URL = "https://api.groq.com/openai/v1/chat/completions";
const TIMEOUT_MS = 25000;

// Used only when wrangler.toml does not set the [vars]. Keep in step with wrangler.toml.
export const DEFAULT_GEMINI_CHAT = "gemini-3.1-flash-lite,gemini-3.5-flash-lite";
export const DEFAULT_GEMINI_QUIZ = "gemini-3.8-flash,gemini-3.5-flash";
export const DEFAULT_GROQ = "openai/gpt-oss-120b,openai/gpt-oss-20b";

export const DEFAULT_GROQ_STT = "whisper-large-v3-turbo,whisper-large-v3";

export function modelList(value, fallback) {
  return String(value || fallback).split(",").map((s) => s.trim()).filter(Boolean);
}

export class ProviderError extends Error {
  constructor(provider, reason, status = 0) {
    super(`${provider}: ${reason}`);
    this.provider = provider;
    this.reason = reason;
    this.status = status;
  }
}

/**
 * Runs one request against Gemini, then Groq if Gemini fails.
 *
 * @param {object} env      Worker environment (keys and model names).
 * @param {object} req
 * @param {"chat"|"quiz"} req.tier   chat = fast model, quiz = stronger model.
 * @param {string} req.system        System instruction.
 * @param {{role:"user"|"assistant", text:string}[]} req.messages  Conversation, last one from the user.
 * @param {boolean} req.json         Ask for a JSON object and parse it.
 * @param {number} req.maxTokens
 * @param {number} req.temperature
 * @param {(data:any) => any} [req.validate]  Turns the parsed JSON into the route's result, or
 *        returns null when it is unusable - which counts as that provider failing, so the next
 *        one is tried. (Valid JSON with no usable content is still a failed answer.)
 * @returns {Promise<{data:any, text:string, provider:string, model:string, fallbackReason:string}>}
 */
export async function callModel(env, req) {
  // Which provider goes first is a setting too: speed and reliability on free tiers change over
  // time, and the order is the main lever on how long a student waits.
  const order = modelList(req.tier === "quiz" ? env.PROVIDER_ORDER_QUIZ : env.PROVIDER_ORDER_CHAT, "gemini,groq");
  const attempts = [];
  for (const provider of order) {
    if (provider === "gemini" && env.GEMINI_API_KEY) {
      const models = req.tier === "quiz"
        ? modelList(env.GEMINI_QUIZ_MODEL, DEFAULT_GEMINI_QUIZ)
        : modelList(env.GEMINI_CHAT_MODEL, DEFAULT_GEMINI_CHAT);
      for (const m of models) attempts.push(["gemini", m, callGemini]);
    }
    if (provider === "groq" && env.GROQ_API_KEY) {
      for (const m of modelList(env.GROQ_MODEL, DEFAULT_GROQ)) attempts.push(["groq", m, callGroq]);
    }
  }
  if (attempts.length === 0) throw new ProviderError("none", "no provider keys configured");

  const failures = [];
  for (const [name, model, call] of attempts) {
    try {
      const result = await call(env, req, model);
      let data = req.json ? parseJsonObject(result.text, name) : null;
      if (req.validate) {
        data = req.validate(data);
        if (data == null) throw new ProviderError(name, "answer had nothing usable in it");
      }
      return {
        ...result,
        data,
        provider: name,
        fallbackReason: failures.map((f) => f.message).join("; "),
      };
    } catch (err) {
      const e = err instanceof ProviderError ? err : new ProviderError(name, String(err && err.message || err));
      failures.push(new ProviderError(`${name}/${model}`, e.reason, e.status));
    }
  }
  const all = new ProviderError("all", failures.map((f) => f.message).join("; "));
  all.failures = failures;
  throw all;
}

// ── Gemini ──────────────────────────────────────────────────────────────────

async function callGemini(env, req, model) {
  const body = {
    systemInstruction: { parts: [{ text: req.system }] },
    contents: req.messages.map((m) => ({
      role: m.role === "assistant" ? "model" : "user",
      parts: [{ text: m.text }],
    })),
    generationConfig: {
      temperature: req.temperature ?? 0.4,
      maxOutputTokens: req.maxTokens ?? 2048,
      ...(req.json ? { responseMimeType: "application/json" } : {}),
      ...thinkingConfig(env, req.tier),
    },
  };

  const res = await post("gemini", `${GEMINI_URL}/${encodeURIComponent(model)}:generateContent`, {
    "content-type": "application/json",
    "x-goog-api-key": env.GEMINI_API_KEY,
  }, body);

  const candidate = res.candidates && res.candidates[0];
  if (!candidate) {
    const blocked = res.promptFeedback && res.promptFeedback.blockReason;
    throw new ProviderError("gemini", blocked ? `prompt blocked (${blocked})` : "no candidates");
  }
  const text = ((candidate.content && candidate.content.parts) || [])
    .filter((p) => !p.thought) // newer models can return their reasoning as separate parts
    .map((p) => p.text || "")
    .join("")
    .trim();
  if (!text) throw new ProviderError("gemini", `empty answer (finishReason ${candidate.finishReason || "?"})`);

  return { text, model };
}

/**
 * How long Gemini may "think" before answering. Newer Gemini models think by default, which made a
 * one-paragraph chat answer take over 20 seconds - far too slow for a student waiting on a phone.
 * Chat needs almost none; a quiz benefits from a little. Set GEMINI_THINKING_CHAT / _QUIZ to ""
 * in wrangler.toml to stop sending the setting (if a model ever rejects it).
 */
function thinkingConfig(env, tier) {
  const level = tier === "quiz"
    ? (env.GEMINI_THINKING_QUIZ ?? "low")
    : (env.GEMINI_THINKING_CHAT ?? "minimal");
  return level ? { thinkingConfig: { thinkingLevel: level } } : {};
}

// ── Groq (OpenAI-compatible) ────────────────────────────────────────────────

async function callGroq(env, req, model) {
  const body = {
    model,
    messages: [
      { role: "system", content: req.system },
      ...req.messages.map((m) => ({ role: m.role, content: m.text })),
    ],
    temperature: req.temperature ?? 0.4,
    max_tokens: req.maxTokens ?? 2048,
    ...(req.json ? { response_format: { type: "json_object" } } : {}),
  };

  const res = await post("groq", GROQ_URL, {
    "content-type": "application/json",
    authorization: `Bearer ${env.GROQ_API_KEY}`,
  }, body);

  const text = (res.choices && res.choices[0] && res.choices[0].message && res.choices[0].message.content || "").trim();
  if (!text) throw new ProviderError("groq", "empty answer");
  return { text, model };
}

// ── Shared ──────────────────────────────────────────────────────────────────

async function post(provider, url, headers, body) {
  let res;
  try {
    res = await fetch(url, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch (err) {
    const timedOut = err && (err.name === "TimeoutError" || err.name === "AbortError");
    throw new ProviderError(provider, timedOut ? "timed out" : `network error (${err && err.message})`);
  }

  if (!res.ok) {
    // Keep the provider's own message short: it goes into logs and the fallback reason.
    let detail = "";
    try { detail = (await res.text()).slice(0, 200); } catch { /* ignore */ }
    const reason = res.status === 429 ? "quota or rate limit reached (429)" : `HTTP ${res.status}`;
    throw new ProviderError(provider, `${reason} ${detail}`.trim(), res.status);
  }

  try {
    return await res.json();
  } catch {
    throw new ProviderError(provider, "response was not JSON");
  }
}

/** Parses a model's JSON answer, tolerating a ```json fence around it. */
export function parseJsonObject(text, provider = "model") {
  let t = String(text || "").trim();
  const fence = t.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i);
  if (fence) t = fence[1];
  let value;
  try {
    value = JSON.parse(t);
  } catch {
    throw new ProviderError(provider, "answer was not valid JSON");
  }
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new ProviderError(provider, "answer was not a JSON object");
  }
  return value;
}

// ── Speech to text (voice search) ───────────────────────────────────────────

const GROQ_STT_URL = "https://api.groq.com/openai/v1/audio/transcriptions";

/**
 * Turns a short recording (WAV bytes) into text: Groq's Whisper first, then Gemini, which can also
 * listen to audio. `hint` is a list of words to expect (the model's part names), which helps with
 * technical terms that a general speech model would otherwise mishear.
 *
 * @returns {Promise<{text:string, provider:string, model:string, fallbackReason:string}>}
 */
export async function transcribe(env, wavBytes, hint) {
  const order = modelList(env.PROVIDER_ORDER_STT, "groq,gemini");
  const attempts = [];
  for (const provider of order) {
    if (provider === "groq" && env.GROQ_API_KEY)
      for (const m of modelList(env.GROQ_STT_MODEL, DEFAULT_GROQ_STT)) attempts.push(["groq", m]);
    if (provider === "gemini" && env.GEMINI_API_KEY)
      for (const m of modelList(env.GEMINI_CHAT_MODEL, DEFAULT_GEMINI_CHAT)) attempts.push(["gemini", m]);
  }
  if (attempts.length === 0) throw new ProviderError("none", "no provider keys configured");

  const language = env.STT_LANGUAGE ?? "en";
  const failures = [];
  for (const [provider, model] of attempts) {
    try {
      const text = provider === "groq"
        ? await groqTranscribe(env, model, wavBytes, hint, language)
        : await geminiTranscribe(env, model, wavBytes, hint);
      return { text: cleanTranscript(text), provider, model, fallbackReason: failures.map((f) => f.message).join("; ") };
    } catch (err) {
      const e = err instanceof ProviderError ? err : new ProviderError(provider, String(err && err.message || err));
      failures.push(new ProviderError(`${provider}/${model}`, e.reason, e.status));
    }
  }
  const all = new ProviderError("all", failures.map((f) => f.message).join("; "));
  all.failures = failures;
  throw all;
}

async function groqTranscribe(env, model, wavBytes, hint, language) {
  const form = new FormData();
  form.append("file", new Blob([wavBytes], { type: "audio/wav" }), "speech.wav");
  form.append("model", model);
  form.append("response_format", "json");
  form.append("temperature", "0");
  if (language) form.append("language", language);
  if (hint) form.append("prompt", hint.slice(0, 600));

  let res;
  try {
    res = await fetch(GROQ_STT_URL, {
      method: "POST",
      headers: { authorization: `Bearer ${env.GROQ_API_KEY}` },
      body: form,
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch (err) {
    const timedOut = err && (err.name === "TimeoutError" || err.name === "AbortError");
    throw new ProviderError("groq", timedOut ? "timed out" : `network error (${err && err.message})`);
  }
  if (!res.ok) {
    let detail = "";
    try { detail = (await res.text()).slice(0, 200); } catch { /* ignore */ }
    throw new ProviderError("groq", `HTTP ${res.status} ${detail}`.trim(), res.status);
  }
  const data = await res.json().catch(() => null);
  if (!data || typeof data.text !== "string") throw new ProviderError("groq", "no text in the answer");
  return data.text;
}

async function geminiTranscribe(env, model, wavBytes, hint) {
  const body = {
    contents: [{
      role: "user",
      parts: [
        { text: "Transcribe this short spoken search query exactly as said. Reply with only the words, nothing else. If nothing is said, reply with nothing." +
                (hint ? ` Words that may come up: ${hint.slice(0, 600)}` : "") },
        { inlineData: { mimeType: "audio/wav", data: toBase64(wavBytes) } },
      ],
    }],
    generationConfig: { temperature: 0, maxOutputTokens: 256, ...thinkingConfig(env, "chat") },
  };
  const res = await post("gemini", `${GEMINI_URL}/${encodeURIComponent(model)}:generateContent`, {
    "content-type": "application/json",
    "x-goog-api-key": env.GEMINI_API_KEY,
  }, body);
  const c = res.candidates && res.candidates[0];
  if (!c) throw new ProviderError("gemini", "no candidates");
  return ((c.content && c.content.parts) || []).filter((p) => !p.thought).map((p) => p.text || "").join("");
}

function toBase64(bytes) {
  const u8 = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  let bin = "";
  for (let i = 0; i < u8.length; i += 0x8000) bin += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000));
  return btoa(bin);
}

/** Trims, drops wrapping quotes, and treats Whisper's well-known "heard silence" phrases as nothing. */
export function cleanTranscript(text) {
  let t = String(text || "").trim().replace(/^["'\u201C\u201D]+|["'\u201C\u201D]+$/g, "").trim();
  if (/^(thank you|thanks for watching|you)[.!]?$/i.test(t)) t = "";
  return t.replace(/[.!?]+$/, "");
}
