---
name: generate-research-images
description: Generate ≤14 Instagram research plan images via Cursor GenerateImage into out/ (not OpenAI Images).
---

# Generate research images

## Rules

- Use **Cursor GenerateImage** only. Never call OpenAI Images / DALL·E / external image APIs.
- Read `plan-prompts.md` for day prompts. Optional reference photos are in `refs/`.
- Write **at most 14** images into `out/` as `day-01.png` … `day-14.png` (jpg/webp also OK).
- Prefer fewer high-quality stills over spam. Cap hard at 14.
- Do not invent secrets, tokens, or brand logos that are not requested.
- If GenerateImage is unavailable / rate-limited (429), stop and say so — do not fake files.

## Output

- Files under `out/` only.
- Short text reply: how many images written.
