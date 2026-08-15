import http from "node:http";
import { Agent } from "@cursor/sdk";

const port = Number(process.env.PORT || 8090);
const defaultModel = process.env.CURSOR_BRIDGE_MODEL || "composer-2.5";

function sendJson(res, status, body) {
  const payload = JSON.stringify(body);
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "content-length": Buffer.byteLength(payload),
  });
  res.end(payload);
}

function scrub(text, apiKey) {
  if (!text || !apiKey) return String(text ?? "");
  return String(text).split(apiKey).join("***");
}

async function readJson(req) {
  const chunks = [];
  for await (const chunk of req) {
    chunks.push(chunk);
  }
  const raw = Buffer.concat(chunks).toString("utf8");
  if (!raw) return {};
  return JSON.parse(raw);
}

async function runAgent({ apiKey, prompt, agentId, model }) {
  const modelId = model || defaultModel;
  /** @type {import("@cursor/sdk").SDKAgent} */
  let agent;
  if (agentId) {
    agent = await Agent.resume(agentId, {
      apiKey,
      model: { id: modelId },
    });
  } else {
    // Cloud no-repo agent — chat specialist without repository checkout.
    agent = await Agent.create({
      apiKey,
      model: { id: modelId },
      cloud: { repos: [] },
    });
  }

  try {
    const run = await agent.send(prompt);
    const result = await run.wait();
    const text =
      (result && typeof result.result === "string" && result.result) ||
      (result && typeof result === "object" && result.result) ||
      "";
    if (!text || !String(text).trim()) {
      throw new Error("empty_agent_result");
    }
    return { agentId: agent.agentId, text: String(text).trim() };
  } finally {
    try {
      await agent.close?.();
    } catch {
      // ignore dispose errors
    }
  }
}

const server = http.createServer(async (req, res) => {
  try {
    if (req.method === "GET" && (req.url === "/health" || req.url === "/health/live" || req.url === "/health/ready")) {
      sendJson(res, 200, { status: "ok" });
      return;
    }

    if (req.method === "POST" && req.url === "/v1/run") {
      const body = await readJson(req);
      const apiKey = body.apiKey;
      const prompt = body.prompt;
      const agentId = body.agentId || undefined;
      const model = body.model || undefined;

      if (!apiKey || typeof apiKey !== "string") {
        sendJson(res, 400, { error: "apiKey_required" });
        return;
      }
      if (!prompt || typeof prompt !== "string") {
        sendJson(res, 400, { error: "prompt_required" });
        return;
      }

      console.log(
        JSON.stringify({
          msg: "cursor_sdk_run",
          resume: Boolean(agentId),
          promptLength: prompt.length,
          model: model || defaultModel,
        }),
      );

      const result = await runAgent({ apiKey, prompt, agentId, model });
      sendJson(res, 200, result);
      return;
    }

    sendJson(res, 404, { error: "not_found" });
  } catch (err) {
    const message = scrub(err?.message || String(err), "");
    console.error(JSON.stringify({ msg: "cursor_sdk_error", error: message }));
    sendJson(res, 502, { error: "sdk_failed", detail: message });
  }
});

server.listen(port, "0.0.0.0", () => {
  console.log(JSON.stringify({ msg: "cursor_sdk_bridge_listen", port }));
});
