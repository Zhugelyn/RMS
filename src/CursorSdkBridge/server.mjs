import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { Agent } from "@cursor/sdk";

const port = Number(process.env.PORT || 8090);
const defaultModel = process.env.CURSOR_BRIDGE_MODEL || "composer-2.5";
const packsRoot = process.env.AGENT_PACKS_ROOT || path.resolve(process.cwd(), "../AgentPacks");

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

function assertSafePackId(packId) {
  if (!packId || typeof packId !== "string" || !/^[a-z_][a-z0-9_-]*$/.test(packId)) {
    throw Object.assign(new Error("invalid_pack_id"), { status: 400 });
  }
}

function loadPackRuntime(packId) {
  assertSafePackId(packId);
  const cwd = path.resolve(packsRoot, packId);
  const rootResolved = path.resolve(packsRoot);
  if (!cwd.startsWith(rootResolved + path.sep) && cwd !== rootResolved) {
    throw Object.assign(new Error("pack_path_escape"), { status: 400 });
  }
  if (!fs.existsSync(cwd) || !fs.statSync(cwd).isDirectory()) {
    throw Object.assign(new Error("pack_not_found"), { status: 404 });
  }

  const packJsonPath = path.join(cwd, "pack.json");
  const mcpJsonPath = path.join(cwd, "mcp.json");
  if (!fs.existsSync(packJsonPath) || !fs.existsSync(mcpJsonPath)) {
    throw Object.assign(new Error("pack_incomplete"), { status: 400 });
  }

  const manifest = JSON.parse(fs.readFileSync(packJsonPath, "utf8"));
  const mcp = JSON.parse(fs.readFileSync(mcpJsonPath, "utf8"));
  if (manifest.id !== packId) {
    throw Object.assign(new Error("pack_id_mismatch"), { status: 400 });
  }

  const allowlist = Array.isArray(mcp.allowlist) ? mcp.allowlist : [];
  // Phase 3: no MCP wiring yet — allowlist must stay empty; never load ambient MCP.
  if (allowlist.length > 0) {
    throw Object.assign(new Error("mcp_allowlist_not_wired"), { status: 400 });
  }

  return {
    packId,
    cwd,
    model: manifest.model || defaultModel,
    allowlist,
    mcpServers: {},
  };
}

async function runAgent({ apiKey, prompt, agentId, model, pack }) {
  const modelId = model || pack?.model || defaultModel;
  /** @type {import("@cursor/sdk").SDKAgent} */
  let agent;

  const createOptions = {
    apiKey,
    model: { id: modelId },
  };

  if (pack) {
    // Local pack runtime: cwd = pack root (AGENTS.md + .cursor/skills). MCP denied unless allowlist wired.
    createOptions.local = {
      cwd: pack.cwd,
      settingSources: ["project"],
    };
    createOptions.mcpServers = pack.mcpServers;
  } else {
    // Legacy Phase 2 path (no pack): cloud no-repo.
    createOptions.cloud = { repos: [] };
  }

  if (agentId) {
    agent = await Agent.resume(agentId, {
      apiKey,
      model: { id: modelId },
      ...(pack
        ? { local: { cwd: pack.cwd, settingSources: ["project"] }, mcpServers: pack.mcpServers }
        : {}),
    });
  } else {
    agent = await Agent.create(createOptions);
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
    return {
      agentId: agent.agentId,
      text: String(text).trim(),
      packId: pack?.packId || null,
      cwd: pack?.cwd || null,
    };
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
      sendJson(res, 200, {
        status: "ok",
        packsRoot,
        packsMounted: fs.existsSync(packsRoot),
      });
      return;
    }

    if (req.method === "POST" && req.url === "/v1/run") {
      const body = await readJson(req);
      const apiKey = body.apiKey;
      const prompt = body.prompt;
      const agentId = body.agentId || undefined;
      const model = body.model || undefined;
      const packId = body.packId || undefined;

      if (!apiKey || typeof apiKey !== "string") {
        sendJson(res, 400, { error: "apiKey_required" });
        return;
      }
      if (!prompt || typeof prompt !== "string") {
        sendJson(res, 400, { error: "prompt_required" });
        return;
      }

      let pack = null;
      if (packId) {
        pack = loadPackRuntime(packId);
      }

      console.log(
        JSON.stringify({
          msg: "cursor_sdk_run",
          resume: Boolean(agentId),
          promptLength: prompt.length,
          model: model || pack?.model || defaultModel,
          packId: pack?.packId || null,
          runtime: pack ? "local-pack" : "cloud-no-repo",
        }),
      );

      const result = await runAgent({ apiKey, prompt, agentId, model, pack });
      sendJson(res, 200, result);
      return;
    }

    sendJson(res, 404, { error: "not_found" });
  } catch (err) {
    const status = err?.status || 502;
    const message = scrub(err?.message || String(err), "");
    console.error(JSON.stringify({ msg: "cursor_sdk_error", error: message, status }));
    sendJson(res, status >= 400 && status < 600 ? status : 502, {
      error: status === 502 ? "sdk_failed" : message,
      detail: message,
    });
  }
});

server.listen(port, "0.0.0.0", () => {
  console.log(JSON.stringify({ msg: "cursor_sdk_bridge_listen", port, packsRoot }));
});
