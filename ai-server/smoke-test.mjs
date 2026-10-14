// Calls the DEPLOYED Worker's routes with sample jet-engine data, using the real AI providers.
// Each run uses a few requests of the free daily quota.
//
//   node smoke-test.mjs https://aurasync-ai.<you>.workers.dev <APP_KEY>
//
// To check the Groq backup on its own, run the Worker locally with only the Groq key (README,
// "Checking the backup") and point this script at http://localhost:8787.

const [, , base, appKey] = process.argv;
if (!base || !appKey) {
  console.error("Usage: node smoke-test.mjs <worker-url> <APP_KEY>");
  process.exit(2);
}

const model = { name: "Jet Engine", context: "Turbofan jet engine" };
const parts = [
  { path: "0/0/4/0", label: "Fan", aliases: ["intake fan"], function: "Draws air into the engine" },
  { path: "0/0/5", label: "Pipes", function: "Carry fuel and oil around the engine" },
];

let failed = 0;

async function call(name, path, body) {
  const started = Date.now();
  try {
    const res = await fetch(base.replace(/\/+$/, "") + path, {
      method: body ? "POST" : "GET",
      headers: { "content-type": "application/json", "x-aurasync-key": appKey },
      body: body ? JSON.stringify(body) : undefined,
    });
    const json = await res.json();
    const ms = Date.now() - started;
    const good = res.ok && json.ok;
    if (!good) failed++;
    console.log(`\n${good ? "PASS" : "FAIL"}  ${name}  (HTTP ${res.status}, ${ms} ms${json.provider ? `, ${json.provider}` : ""})`);
    console.log(JSON.stringify(json, null, 2).split("\n").slice(0, 25).join("\n"));
  } catch (err) {
    failed++;
    console.log(`\nFAIL  ${name}: ${err.message}`);
  }
}

await call("health", "/health");
await call("ask about a part", "/ask", {
  model, part: { label: "Fan", function: "Draws air into the engine", material: "Titanium alloy" },
  question: "Why is the fan so big compared to the rest of the engine?",
});
await call("ask about the whole model", "/ask", { model, question: "What is this?" });
await call("find by meaning", "/find", { query: "the part that sucks air in", parts });
await call("quiz", "/quiz", { model, parts, count: 3, difficulty: "easy" });

console.log(failed ? `\n${failed} check(s) failed.` : "\nAll checks passed.");
process.exit(failed ? 1 : 0);
