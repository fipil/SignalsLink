import fs from "fs/promises";
import path from "node:path";

// Cesty pøizpùsob svému projektu
const langDir = path.resolve("SignalsLink/assets/signalslink/lang");
const htmlPath = path.join(langDir, "signalslink.html");
const csPath = path.join(langDir, "cs.json");

// prompt soubor
const promptPath = path.resolve(".github/prompts/localize-signalslink.md");

// Jazyky
const targetLangs = ["de", "en", "es", "fr", "it", "pl", "pt", "ru", "sk"];

// Odstranìní BOM (U+FEFF) ze stringu
function stripBOM(str) {
    if (typeof str !== "string") return str;
    return str.replace(/^\uFEFF/, "");
}

// Rekurzivní odstranìní BOM ze všech stringù v objektu/array
function stripBOMDeep(value) {
    if (typeof value === "string") {
        return stripBOM(value);
    }
    if (Array.isArray(value)) {
        return value.map(stripBOMDeep);
    }
    if (value && typeof value === "object") {
        const out = {};
        for (const [k, v] of Object.entries(value)) {
            out[k] = stripBOMDeep(v);
        }
        return out;
    }
    return value;
}

async function callOpenAI({ systemPrompt, payload }) {
    const apiKey = process.env.OPENAI_API_KEY;
    if (!apiKey) {
        throw new Error("Chybí promìnná prostøedí OPENAI_API_KEY");
    }

    const response = await fetch("https://api.openai.com/v1/chat/completions", {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${apiKey}`,
        },
        body: JSON.stringify({
            model: "gpt-5-mini",
            response_format: { type: "json_object" },
            messages: [
                { role: "system", content: systemPrompt },
                { role: "user", content: JSON.stringify(payload) },
            ],
        }),
    });

    if (!response.ok) {
        const text = await response.text().catch(() => "");
        throw new Error(
            `OpenAI API error: ${response.status} ${response.statusText} - ${text}`,
        );
    }

    const data = await response.json();

    const rawContent =
        data.choices?.[0]?.message?.content ??
        (() => {
            throw new Error("OpenAI nevrátilo žádný obsah v message.content");
        })();

    // Pro jistotu odstraníme BOM i z contentu
    const content = stripBOM(rawContent);

    let result;
    try {
        result = JSON.parse(content);
    } catch (e) {
        console.error("Odpovìï modelu není validní JSON:", content);
        throw e;
    }

    // Rekurzivnì odstraníme BOM ze všech stringù
    return stripBOMDeep(result);
}

async function main() {
    // naèteme vstupy a rovnou z nich odstraníme pøípadný BOM
    const [rawHtml, rawCsText, rawPrompt] = await Promise.all([
        fs.readFile(htmlPath, "utf8"),
        fs.readFile(csPath, "utf8"),
        fs.readFile(promptPath, "utf8"),
    ]);

    const html = stripBOM(rawHtml);
    const csText = stripBOM(rawCsText);
    const systemPrompt = stripBOM(rawPrompt);

    const cs = JSON.parse(csText);

    const payload = {
        html,
        cs,
        targetLanguages: targetLangs,
    };

    console.log("Volám OpenAI…");
    const result = await callOpenAI({ systemPrompt, payload });

    if (!result.cs || typeof result.cs !== "object") {
        throw new Error("Výsledek neobsahuje platný objekt 'cs'");
    }

    // cs.json – ještì jednou projistotu projedeme stripBOMDeep
    const cleanedCs = stripBOMDeep(result.cs);
    await fs.writeFile(
        csPath,
        JSON.stringify(cleanedCs, null, 2) + "\n",
        "utf8",
    );

    // ostatní jazyky
    for (const lang of targetLangs) {
        const data = result[lang];
        if (!data || typeof data !== "object") {
            console.warn(
                `Varování: výsledek pro jazyk '${lang}' chybí nebo není objekt, pøeskoèeno.`,
            );
            continue;
        }

        const cleanedLang = stripBOMDeep(data);
        const langPath = path.join(langDir, `${lang}.json`);
        await fs.writeFile(
            langPath,
            JSON.stringify(cleanedLang, null, 2) + "\n",
            "utf8",
        );
    }

    console.log("Lokalizace úspìšnì aktualizována (BOM odstranìn).");
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
