import fs from "node:fs";
import path from "node:path";

const IMAGE_EXTS = new Set([".png", ".jpg", ".jpeg", ".webp"]);

/**
 * Collect png|jpg|webp under cwd (prefer out/), created/modified at or after sinceMs.
 * Cap defaults to 14 (ADR-011 research plan days).
 */
export function collectImages(cwd, { sinceMs = 0, cap = 14 } = {}) {
  const root = path.resolve(cwd);
  if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
    return [];
  }

  const outDir = path.join(root, "out");
  const scanRoots = [];
  if (fs.existsSync(outDir) && fs.statSync(outDir).isDirectory()) {
    scanRoots.push(outDir);
  }

  /** @type {{ abs: string, mtimeMs: number }[]} */
  const found = [];
  const seen = new Set();

  const scanOne = (scanRoot, skipOutPrefix) => {
    walk(scanRoot, (abs, st) => {
      if (seen.has(abs)) return;
      const ext = path.extname(abs).toLowerCase();
      if (!IMAGE_EXTS.has(ext)) return;
      const rel = path.relative(root, abs);
      if (rel.split(path.sep)[0] === "refs") return;
      if (skipOutPrefix && rel.split(path.sep)[0] === "out") return;
      if (sinceMs > 0 && st.mtimeMs + 1 < sinceMs) return;
      seen.add(abs);
      found.push({ abs, mtimeMs: st.mtimeMs });
    });
  };

  if (scanRoots.length > 0) {
    scanOne(outDir, false);
  }
  if (found.length === 0) {
    scanOne(root, true);
  }

  found.sort((a, b) => a.mtimeMs - b.mtimeMs || a.abs.localeCompare(b.abs));
  const limited = found.slice(0, Math.max(0, Math.min(cap, 14)));
  return limited.map((f) => path.relative(root, f.abs).split(path.sep).join("/"));
}

function walk(dir, onFile) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const ent of entries) {
    const abs = path.join(dir, ent.name);
    if (ent.isDirectory()) {
      if (ent.name === "node_modules" || ent.name === ".git") continue;
      walk(abs, onFile);
      continue;
    }
    if (!ent.isFile()) continue;
    try {
      const st = fs.statSync(abs);
      onFile(abs, st);
    } catch {
      // ignore unreadable
    }
  }
}

export function isImageToolSoftFail(message) {
  const m = String(message ?? "");
  return (
    /\b429\b/.test(m) ||
    /rate.?limit/i.test(m) ||
    /image-?tool-?missing/i.test(m) ||
    /GenerateImage/i.test(m) ||
    /tool.?not.?(available|found|supported)/i.test(m) ||
    /images?.?tool/i.test(m)
  );
}

export const IMAGE_CAP = 14;
