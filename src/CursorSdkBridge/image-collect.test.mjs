import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { collectImages, isImageToolSoftFail, IMAGE_CAP } from "./image-collect.mjs";

const root = fs.mkdtempSync(path.join(os.tmpdir(), "img-collect-"));
fs.mkdirSync(path.join(root, "out"));
fs.mkdirSync(path.join(root, "refs"));
fs.writeFileSync(path.join(root, "refs", "a.jpg"), "x");
for (let i = 1; i <= 16; i++) {
  fs.writeFileSync(path.join(root, "out", `day-${String(i).padStart(2, "0")}.png`), "x");
}

const images = collectImages(root, { cap: IMAGE_CAP });
if (images.length !== 14) {
  console.error("expected 14 got", images.length, images);
  process.exit(1);
}
if (!images.every((p) => p.startsWith("out/"))) {
  console.error("expected out/ prefix", images);
  process.exit(1);
}
if (!isImageToolSoftFail("HTTP 429 rate limit")) {
  console.error("429 soft-fail detect failed");
  process.exit(1);
}
if (!isImageToolSoftFail("GenerateImage tool not available")) {
  console.error("GenerateImage soft-fail detect failed");
  process.exit(1);
}
fs.rmSync(root, { recursive: true, force: true });
console.log(JSON.stringify({ ok: true, cap: IMAGE_CAP, count: images.length }));
