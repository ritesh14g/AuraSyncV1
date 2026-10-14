# AuraSync AI server

A small Cloudflare Worker that sits between the app and the AI. The app never holds an AI key;
this Worker does, as encrypted secrets.

| Route | Used for | Answers with |
|---|---|---|
| `GET /health` | Checking it is up and configured | which providers have keys |
| `POST /ask` | Questions about a part or the whole model (Phase 3) | `answer`, up to 3 `suggestions` |
| `POST /find` | Finding a part by meaning (Phase 4) | up to 5 `matches` |
| `POST /quiz` | Quiz generation (Phase 11) | `questions` with 4 options each |
| `POST /transcribe` | Voice search: a short WAV recording to text (Phase 4) | `text` (Groq Whisper, then Gemini) |

Two free AI providers cover for each other. For **chat and search, Groq answers first** (about
1.4 s, against 7–23 s for Gemini's free models, measured 2026-10-09). For **quizzes, Gemini answers
first**: the teacher waits once and quality matters more. If the first choice fails for any reason
(model retired, daily quota used up, outage, timeout, an unusable answer), the next model or
provider takes the same request. Both are free, so the worst a misused key can do is use up the
day's quota. No bill is possible.

---

## One-time setup (about 15 minutes)

You need three free accounts. None asks for a card.

### 1. Get the two AI keys

- **Gemini:** <https://aistudio.google.com/apikey> → *Create API key*. Copy it.
- **Groq:** <https://console.groq.com/keys> → *Create API Key*. Copy it.

### 2. Make up an app key

Any long random string, for example from PowerShell:

```powershell
-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 40 | % {[char]$_})
```

This goes into both the Worker and the app. It only stops casual misuse (it can be dug out of the
APK), which is fine because nothing here costs money.

### 3. Deploy

From this folder (`ai-server`):

```bash
npx wrangler login                          # opens the browser; sign up / sign in to Cloudflare
npx wrangler secret put GEMINI_API_KEY      # paste the Gemini key when asked
npx wrangler secret put GROQ_API_KEY        # paste the Groq key
npx wrangler secret put APP_KEY             # paste your app key
npx wrangler deploy
```

The last line prints the Worker's address, like `https://aurasync-ai.<your-name>.workers.dev`.
**Send me that address.** The app needs it in Phase 3. (Never send the keys.)

> The first `secret put` may offer to create the Worker first. Say yes.

### 4. Check it works

```bash
node smoke-test.mjs https://aurasync-ai.<your-name>.workers.dev <your app key>
```

You should see five `PASS` lines and real answers about a jet engine: chat and search from
`groq` in about 1–2 seconds, the quiz from `gemini`. That uses about 5 requests of the free quota.

> Run every `npx wrangler ...` and `node smoke-test.mjs` command **from inside `ai-server`**. From
> the folder above it, Wrangler cannot find `wrangler.toml` and says the Worker name is missing.

---

## Checking the backup (Groq) on its own

Create a file `.dev.vars` in this folder (it is never uploaded):

```
GROQ_API_KEY=<your Groq key>
APP_KEY=<your app key>
```

(The name must be `GROQ_API_KEY`, with a Q. `GROK_API_KEY` is ignored.)

Then run the Worker on your own computer, with no Gemini key, in one terminal:

```bash
npx wrangler dev
```

and in another:

```bash
node smoke-test.mjs http://localhost:8787 <your app key>
```

Every answer should say `groq`. Delete `.dev.vars` afterwards if you like.

---

## Everyday commands

| Command | What it does |
|---|---|
| `npm test` | Offline tests with fake AI providers. No keys, no network, no quota used. |
| `npm run check` | Builds the Worker without deploying it. |
| `npx wrangler deploy` | Publishes changes. |
| `npx wrangler tail` | Shows the live log, including why Gemini fell back to Groq. |

## Changing models

The models and the provider order are in `wrangler.toml` under `[vars]`. Each model setting is a
comma-separated list, tried in order, so a retired model (it answers 404) hands over to the next
one by itself. Change a setting and redeploy. To see which models your keys can use:

```bash
curl "https://generativelanguage.googleapis.com/v1beta/models?key=<your Gemini key>"
curl https://api.groq.com/openai/v1/models -H "Authorization: Bearer <your Groq key>"
```

## Privacy

On these free tiers, Google may use what is sent to improve its products. The app shows a short
notice in the chat window; students should not type personal information.
