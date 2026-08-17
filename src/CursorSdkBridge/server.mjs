import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { Agent } from "@cursor/sdk";
import { collectImages, isImageToolSoftFail, IMAGE_CAP } from "./image-collect.mjs";

const port = Number(process.env.PORT || 8090);
const defaultModel = process.env.CURSOR_BRIDGE_MODEL || "composer-2.5";
const packsRoot = process.env.AGENT_PACKS_ROOT || path.resolve(process.cwd(), "../AgentPacks");
const imageVolumeRoot = process.env.RESEARCH_IMAGE_VOLUME
  ? path.resolve(process.env.RESEARCH_IMAGE_VOLUME)
  : null;

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

/**
 * Research GenerateImage job: cwd must stay under RESEARCH_IMAGE_VOLUME (ADR-011).
 * Pack skills/AGENTS are prepared into that cwd by assistant-api.
 */
function resolveLocalCwd(localCwd) {
  if (!localCwd || typeof localCwd !== "string") {
    throw Object.assign(new Error("local_cwd_required"), { status: 400 });
  }
  if (!imageVolumeRoot) {
    throw Object.assign(new Error("image_volume_not_configured"), { status: 400 });
  }
  const resolved = path.resolve(localCwd);
  if (!resolved.startsWith(imageVolumeRoot + path.sep) && resolved !== imageVolumeRoot) {
    throw Object.assign(new Error("local_cwd_escape"), { status: 400 });
  }
  if (!fs.existsSync(resolved) || !fs.statSync(resolved).isDirectory()) {
    throw Object.assign(new Error("local_cwd_missing"), { status: 400 });
  }
  return resolved;
}

async function runAgent({ apiKey, prompt, agentId, model, pack, forceLocalCwd }) {
  const modelId = model || pack?.model || defaultModel;
  /** @type {import("@cursor/sdk").SDKAgent} */
  let agent;

  const createOptions = {
    apiKey,
    model: { id: modelId },
  };

  const cwd = forceLocalCwd || pack?.cwd || null;
  if (cwd) {
    // Local pack / research-media runtime: cwd has AGENTS.md + .cursor/skills.
    createOptions.local = {
      cwd,
      settingSources: ["project"],
    };
    createOptions.mcpServers = pack?.mcpServers || {};
  } else {
    // Legacy Phase 2 path (no pack): cloud no-repo. Regular /v1/chat may stay here.
    createOptions.cloud = { repos: [] };
  }

  if (agentId) {
    agent = await Agent.resume(agentId, {
      apiKey,
      model: { id: modelId },
      ...(cwd
        ? { local: { cwd, settingSources: ["project"] }, mcpServers: pack?.mcpServers || {} }
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
      cwd: cwd || null,
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
        imageVolume: imageVolumeRoot,
        imageVolumeMounted: Boolean(imageVolumeRoot && fs.existsSync(imageVolumeRoot)),
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
      const collect = Boolean(body.collectImages);
      const localCwdRaw = body.localCwd || undefined;
      const imageCap = Math.min(
        IMAGE_CAP,
        Math.max(0, Number.isFinite(Number(body.imageCap)) ? Number(body.imageCap) : IMAGE_CAP),
      );

      if (!apiKey || typeof apiKey !== "string") {
        sendJson(res, 400, { error: "apiKey_required" });
        return;
      }
      if (!prompt || typeof prompt !== "string") {
        sendJson(res, 400, { error: "prompt_required" });
        return;
      }

      let pack = null;
      if (packId && !collect) {
        pack = loadPackRuntime(packId);
      } else if (packId && collect) {
        // Research image job: load pack only for model defaults; cwd overridden by volume.
        try {
          pack = loadPackRuntime(packId);
        } catch {
          pack = { packId, cwd: null, model: defaultModel, mcpServers: {} };
        }
      }

      let forceLocalCwd = null;
      if (collect || localCwdRaw) {
        forceLocalCwd = resolveLocalCwd(localCwdRaw);
      }

      console.log(
        JSON.stringify({
          msg: "cursor_sdk_run",
          resume: Boolean(agentId),
          promptLength: prompt.length,
          model: model || pack?.model || defaultModel,
          packId: pack?.packId || null,
          runtime: forceLocalCwd ? "local-images" : pack ? "local-pack" : "cloud-no-repo",
          collectImages: collect,
          imageCap,
        }),
      );

      const sinceMs = Date.now() - 1000;
      try {
        const result = await runAgent({
          apiKey,
          prompt,
          agentId,
          model,
          pack,
          forceLocalCwd,
        });

        const response = {
          agentId: result.agentId,
          text: result.text,
          packId: result.packId,
        };

        if (collect && forceLocalCwd) {
          response.images = collectImages(forceLocalCwd, { sinceMs, cap: imageCap });
        }

        sendJson(res, 200, response);
        return;
      } catch (err) {
        const message = scrub(err?.message || String(err), apiKey);
        if (collect && isImageToolSoftFail(message)) {
          const images =
            forceLocalCwd != null
              ? collectImages(forceLocalCwd, { sinceMs, cap: imageCap })
              : [];
          sendJson(res, 200, {
            agentId: agentId || "soft-fail",
            text: "",
            packId: pack?.packId || packId || null,
            images,
            error: "image-tool-missing",
          });
          return;
        }
        throw err;
      }
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
  console.log(
    JSON.stringify({
      msg: "cursor_sdk_bridge_listen",
      port,
      packsRoot,
      imageVolume: imageVolumeRoot,
    }),
  );
});
