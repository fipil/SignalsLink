import fs from "fs/promises";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

/**
 * SignalsLink localization pipeline (NO external deps; works on Node 20+)
 *
 * Step 1 (NO AI):
 * - Read signalslink.html
 * - Extract <translation key="...">...</translation>
 * - Flatten inner HTML to a single line (remove \r/\n only)
 * - Escape only closing tags: </ -> <\/
 * - Write back into cs.json (UTF-8, no BOM, indent=2)
 *
 * Step 2 (AI):
 * - Determine which cs keys changed vs previous commit (HEAD^) + missing keys per language
 * - For each target language, translate only those keys from cs.json
 * - Preserve HTML tags/attributes/order, keep values one-line
 * - Save <lang>.json as normal key->value dictionary (same shape as cs.json)
 */

/** =========================
 *  Config (paths)
 *  ========================= */
const defaultLangDir = "SignalsLink/assets/signalslink/lang";

const langDir = path.resolve(process.env.SIGNALSLINK_LANGDIR || defaultLangDir);
const htmlPath = path.resolve(
    process.env.SIGNALSLINK_HTML || path.join(defaultLangDir, "signalslink.html"),
);
const csPath = path.resolve(process.env.SIGNALSLINK_CS || path.join(defaultLangDir, "cs.json"));

// Prompt only for translations
const translatePromptPath = path.resolve(
    process.env.SIGNALSLINK_TRANSLATE_PROMPT || ".github/prompts/localize-signalslink-translate.md",
);

// Target languages (comma-separated override supported)
const targetLangs = (process.env.SIGNALSLINK_LANGS
    ? process.env.SIGNALSLINK_LANGS.split(",").map((s) => s.trim()).filter(Boolean)
    : ["de", "en", "es", "fr", "it", "pl", "pt", "ru", "sk"]);

// How many keys per request per language
const CHUNK_SIZE = Number(process.env.SIGNALSLINK_CHUNK_SIZE || 30);

// If true, remove keys from <lang>.json that no longer exist in cs.json
const PRUNE_EXTRA_KEYS = (process.env.SIGNALSLINK_PRUNE ?? "1") !== "0";

// Model
const OPENAI_MODEL = process.env.OPENAI_MODEL || "gpt-5.1";

// Request timeout (AbortController) for OpenAI calls
const FETCH_TIMEOUT_MS = Number(process.env.SIGNALSLINK_FETCH_TIMEOUT_MS || 600_000);

/** =========================
 *  BOM helpers
 *  ========================= */
function stripBOM(str) {
    if (typeof str !== "string") return str;
    return str.replace(/^\uFEFF/, "");
}

function stripBOMDeep(value) {
    if (typeof value === "string") return stripBOM(value);
    if (Array.isArray(value)) return value.map(stripBOMDeep);
    if (value && typeof value === "object") {
        const out = {};
        for (const [k, v] of Object.entries(value)) out[k] = stripBOMDeep(v);
        return out;
    }
    return value;
}

async function readTextUtf8(filePath) {
    const raw = await fs.readFile(filePath, "utf8");
    return stripBOM(raw);
}

async function readJsonUtf8(filePath) {
    const text = await readTextUtf8(filePath);
    return stripBOMDeep(JSON.parse(text));
}

async function writeJsonUtf8(filePath, obj) {
    const cleaned = stripBOMDeep(obj);
    await fs.writeFile(filePath, JSON.stringify(cleaned, null, 2) + "\n", "utf8");
}

/** =========================
 *  HTML -> translations extraction
 *  ========================= */
function extractTranslations(html) {
    const re = /<translation\s+key="([^"]+)"[^>]*>([\s\S]*?)<\/translation>/g;
    const map = new Map();
    let m;
    while ((m = re.exec(html)) !== null) {
        map.set(m[1], m[2]);
    }
    return map;
}

function toOneLineHtml(s) {
    // Only remove CR/LF, keep all other characters exactly as-is.
    return s.replace(/\r?\n/g, "");
}

function escapeClosingTagsOnly(s) {
    // Only escape closing tags: </ -> <\/
    return s.replace(/<\//g, "<\\/");
}

/** =========================
 *  Chunk + validations
 *  ========================= */
function chunkArray(arr, size) {
    const out = [];
    for (let i = 0; i < arr.length; i += size) out.push(arr.slice(i, i + size));
    return out;
}

function validateOneLineStrings(obj, label) {
    for (const [k, v] of Object.entries(obj)) {
        if (typeof v !== "string") throw new Error(`${label}: key '${k}' není string`);
        if (/\r|\n/.test(v)) throw new Error(`${label}: key '${k}' obsahuje \\r nebo \\n`);
    }
}

/** =========================
 *  Git helpers (detect changes in cs.json between commits)
 *  ========================= */
async function tryReadFileFromGit(ref, fileAbsolutePath) {
    const repoRoot = process.cwd();
    const rel = path.relative(repoRoot, fileAbsolutePath).replace(/\\/g, "/");

    try {
        const { stdout } = await execFileAsync("git", ["show", `${ref}:${rel}`], {
            maxBuffer: 50 * 1024 * 1024,
        });
        return stripBOM(stdout);
    } catch {
        return null;
    }
}

async function tryReadPreviousCsJson() {
    const text = await tryReadFileFromGit("HEAD^", csPath);
    if (!text) return null;
    try {
        return stripBOMDeep(JSON.parse(text));
    } catch {
        return null;
    }
}

function computeChangedKeysVsPrevious(previousCs, currentCs) {
    // If we can't read previous snapshot, treat all current keys as changed.
    if (!previousCs) return Object.keys(currentCs);

    const changed = [];
    const keys = new Set([...Object.keys(previousCs), ...Object.keys(currentCs)]);

    for (const k of keys) {
        if (!(k in currentCs)) continue; // removed keys don't need translating
        if (!(k in previousCs) || previousCs[k] !== currentCs[k]) changed.push(k);
    }

    return changed;
}

/** =========================
 *  fetch timeout + retry
 *  ========================= */
async function fetchWithTimeout(url, options, timeoutMs) {
    const controller = new AbortController();
    const id = setTimeout(() => controller.abort(new Error("Request timeout")), timeoutMs);
    try {
        return await fetch(url, { ...options, signal: controller.signal });
    } finally {
        clearTimeout(id);
    }
}

function isRetryableError(err) {
    const code = err?.cause?.code || err?.code;
    return [
        "UND_ERR_HEADERS_TIMEOUT",
        "UND_ERR_CONNECT_TIMEOUT",
        "UND_ERR_SOCKET",
        "ECONNRESET",
        "ETIMEDOUT",
        "EAI_AGAIN",
    ].includes(code);
}

async function sleep(ms) {
    return new Promise((r) => setTimeout(r, ms));
}

/** =========================
 *  OpenAI translate call
 *  ========================= */
async function callOpenAITranslate({ systemPrompt, targetLang, items }) {
    const apiKey = process.env.OPENAI_API_KEY;
    if (!apiKey) throw new Error("Chybí promìnná prostøedí OPENAI_API_KEY");

    const payload = { targetLanguage: targetLang, items };

    const res = await fetchWithTimeout(
        "https://api.openai.com/v1/chat/completions",
        {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                Authorization: `Bearer ${apiKey}`,
            },
            body: JSON.stringify({
                model: OPENAI_MODEL,
                response_format: { type: "json_object" },
                messages: [
                    { role: "system", content: systemPrompt },
                    { role: "user", content: JSON.stringify(payload) },
                ],
            }),
        },
        FETCH_TIMEOUT_MS,
    );

    if (!res.ok) {
        const text = await res.text().catch(() => "");
        throw new Error(`OpenAI API error: ${res.status} ${res.statusText} - ${text}`);
    }

    const data = await res.json();
    const content = stripBOM(data?.choices?.[0]?.message?.content ?? "");

    let parsed;
    try {
        parsed = JSON.parse(content);
    } catch {
        console.error("Model vrátil nevalidní JSON:", content);
        throw new Error("Model vrátil nevalidní JSON");
    }

    return stripBOMDeep(parsed);
}

async function callOpenAITranslateWithRetry(args, maxAttempts = 4) {
    let attempt = 0;
    while (true) {
        attempt++;
        try {
            return await callOpenAITranslate(args);
        } catch (err) {
            if (attempt >= maxAttempts || !isRetryableError(err)) throw err;
            const wait = Math.min(30_000, 1_000 * 2 ** (attempt - 1));
            console.warn(
                `OpenAI call failed (attempt ${attempt}/${maxAttempts}). Retrying in ${wait}ms...`,
                err?.cause?.code || err?.code || err,
            );
            await sleep(wait);
        }
    }
}

/** =========================
 *  STEP 1: Seed CS (NO AI)
 *  ========================= */
async function seedCsFromHtml() {
    const [htmlRaw, csOld] = await Promise.all([readTextUtf8(htmlPath), readJsonUtf8(csPath)]);

    const translations = extractTranslations(htmlRaw);
    const csNew = { ...csOld };

    for (const [key, inner] of translations.entries()) {
        const oneLine = toOneLineHtml(inner);
        const escaped = escapeClosingTagsOnly(oneLine);
        csNew[key] = escaped;
    }

    await writeJsonUtf8(csPath, csNew);

    console.log(
        `CS seed hotový: našel jsem ${translations.size} <translation> blokù a propsal je do cs.json.`,
    );

    return { csNew };
}

/** =========================
 *  STEP 2: Translate per language (AI)
 *  ========================= */
async function translateAll({ csNew, changedKeys }) {
    const translatePrompt = await readTextUtf8(translatePromptPath);

    for (const lang of targetLangs) {
        const langPath = path.join(langDir, `${lang}.json`);

        let langJson = {};
        try {
            langJson = await readJsonUtf8(langPath);
        } catch (e) {
            if (e?.code === "ENOENT") langJson = {};
            else throw e;
        }

        // Keep language files aligned with cs keys (optional but recommended)
        if (PRUNE_EXTRA_KEYS) {
            for (const k of Object.keys(langJson)) {
                if (!(k in csNew)) delete langJson[k];
            }
        }

        // Translate: keys changed in cs since previous commit + keys missing in this language.
        const missingKeys = Object.keys(csNew).filter((k) => !(k in langJson));
        const keysToTranslate = Array.from(new Set([...changedKeys, ...missingKeys]));

        if (keysToTranslate.length === 0) {
            console.log(`[${lang}] nic k pøekladu`);
            continue;
        }

        console.log(`[${lang}] klíèù k pøekladu: ${keysToTranslate.length}`);

        const chunks = chunkArray(keysToTranslate, CHUNK_SIZE);

        for (let i = 0; i < chunks.length; i++) {
            const keysChunk = chunks[i];

            const items = {};
            for (const k of keysChunk) items[k] = csNew[k];

            validateOneLineStrings(items, `Input CS for ${lang}`);

            console.log(`[${lang}] dávka ${i + 1}/${chunks.length} (${keysChunk.length} keys)`);

            const result = await callOpenAITranslateWithRetry({
                systemPrompt: translatePrompt,
                targetLang: lang,
                items,
            });

            if (!result || typeof result !== "object" || Array.isArray(result)) {
                throw new Error(`[${lang}] Model nevrátil objekt key->value`);
            }

            // Fail-fast: model must return exactly the same keys
            const inputKeys = Object.keys(items).sort();
            const outputKeys = Object.keys(result).sort();
            if (inputKeys.length !== outputKeys.length) {
                throw new Error(
                    `[${lang}] Model vrátil jiný poèet klíèù než vstup (${outputKeys.length} vs ${inputKeys.length})`,
                );
            }
            for (let j = 0; j < inputKeys.length; j++) {
                if (inputKeys[j] !== outputKeys[j]) {
                    throw new Error(
                        `[${lang}] Model vrátil jiné klíèe než vstup (napø. '${outputKeys[j]}' místo '${inputKeys[j]}')`,
                    );
                }
            }

            validateOneLineStrings(result, `Output ${lang}`);

            // Safety: enforce </ -> <\/ even if model forgets
            for (const [k, v] of Object.entries(result)) {
                langJson[k] = escapeClosingTagsOnly(v);
            }
        }

        await writeJsonUtf8(langPath, langJson);
        console.log(`[${lang}] uloženo: ${langPath}`);
    }
}

/** =========================
 *  MAIN
 *  ========================= */
async function main() {
    const previousCs = await tryReadPreviousCsJson();

    const { csNew } = await seedCsFromHtml();

    const changedKeys = computeChangedKeysVsPrevious(previousCs, csNew);

    if (changedKeys.length === 0) {
        console.log("cs.json se oproti pøedchozímu commitu nezmìnil, pøeklady se nepouští.");
        return;
    }

    await translateAll({ csNew, changedKeys });
    console.log("Hotovo.");
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
