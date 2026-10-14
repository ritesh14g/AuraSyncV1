// System prompts and the user-message builders for each route.
//
// Every route asks for a JSON object, because the app parses answers with Unity's JsonUtility,
// which is strict. Answers are plain text inside that JSON: the app's TextMeshPro labels would
// show Markdown symbols (** and #) literally.

export const ASK_SYSTEM = `You are the study assistant inside AuraSync, an app where students explore 3D models of real objects (engines, buildings, machines) on their phones.

A student has selected a part of a model, or the whole model, and is asking about it. You are given facts about it.

How to answer:
- Ground your answer in the facts given. Where they do not cover the question, use general engineering knowledge, and say plainly when you are unsure.
- Be clear and friendly, for a college student. At most 120 words. Plain text only: no Markdown, no bullet symbols, no headings.
- If the question is unrelated to the model or to engineering, say briefly that you can only help with this model, and suggest a related question instead.
- Then suggest up to 3 short follow-up questions the student could tap next (each under 60 characters).

Reply with only this JSON object:
{"answer": "<your answer>", "suggestions": ["<question>", "<question>", "<question>"]}`;

export const FIND_SYSTEM = `You help students find a part of a 3D model by describing it.

You get a numbered list of the model's parts and a search query. The query may name the part, describe what it does ("the part that burns fuel"), or describe where it is.

Return the parts the query refers to, best match first, at most 5. Only use paths from the list. If nothing fits, return an empty list.

Reply with only this JSON object:
{"matches": [{"path": "<path from the list>", "why": "<under 12 words>"}]}`;

export const QUIZ_SYSTEM = `You write multiple-choice quiz questions for college students who have just explored a 3D model in class.

Rules:
- Base every question on the facts given about the model and its parts. Do not test facts that are not given.
- Each question has exactly 4 options and exactly one correct answer. Make the wrong options plausible.
- Vary the correct option's position.
- Where a question is about one specific part, give that part's path in "partPath"; otherwise use "".
- Keep each question under 160 characters and each option under 80. Plain text only.
- Give a one-sentence explanation of the correct answer.

Reply with only this JSON object:
{"questions": [{"question": "...", "options": ["...", "...", "...", "..."], "answer": <index 0-3 of the correct option>, "partPath": "...", "explanation": "..."}]}`;

/** Facts about the model and (optionally) one part, as plain lines. */
export function factsBlock(model, part) {
  const lines = [];
  lines.push(`Model: ${model.name || "unnamed model"}`);
  if (model.context) lines.push(`What it is: ${model.context}`);
  if (model.summary) lines.push(`About the model: ${model.summary}`);
  if (part) {
    lines.push("");
    lines.push(`Selected part: ${part.label || "unlabelled part"}`);
    if (part.function) lines.push(`What it does: ${part.function}`);
    if (part.material) lines.push(`Material: ${part.material}`);
    if (part.connectsTo && part.connectsTo.length) lines.push(`Connected to: ${part.connectsTo.join(", ")}`);
  } else {
    lines.push("");
    lines.push("The student is asking about the whole model, not one part.");
  }
  return lines.join("\n");
}

export function findUserMessage(query, parts) {
  const list = parts
    .map((p) => {
      const bits = [`${p.path}: ${p.label || "(unlabelled)"}`];
      if (p.aliases && p.aliases.length) bits.push(`also called ${p.aliases.join(", ")}`);
      if (p.function) bits.push(p.function);
      return bits.join(" | ");
    })
    .join("\n");
  return `Parts:\n${list}\n\nSearch query: ${query}`;
}

export function quizUserMessage(model, parts, count, difficulty) {
  const list = parts
    .map((p) => {
      const bits = [`- path ${p.path}: ${p.label || "(unlabelled)"}`];
      if (p.function) bits.push(`does: ${p.function}`);
      if (p.material) bits.push(`material: ${p.material}`);
      if (p.connectsTo && p.connectsTo.length) bits.push(`connects to: ${p.connectsTo.join(", ")}`);
      return bits.join("; ");
    })
    .join("\n");
  return [
    `Model: ${model.name || "unnamed model"}`,
    model.context ? `What it is: ${model.context}` : "",
    model.summary ? `About the model: ${model.summary}` : "",
    "",
    parts.length ? `Parts:\n${list}` : "No part details: ask about the model as a whole.",
    "",
    `Write ${count} questions. Difficulty: ${difficulty}.`,
  ].filter((l) => l !== "").join("\n");
}
