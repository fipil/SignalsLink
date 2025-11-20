import fs from "fs/promises";
import path from "node:path";

// Cesty pøizpùsob svému projektu
const langDir = path.resolve("SignalsLink.EP/assets/signalslinkep/lang");
const htmlPath = path.join(langDir, "signalslinkep.html");
const csPath = path.join(langDir, "cs.json");

// prompt soubor
const promptPath = path.resolve(".github/prompts/localize-signalslinkep.md");

// Jazyky
const targetLangs = ["de", "en", "es", "fr", "it", "pl", "pt", "ru", "sk"];

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

    const content =
        data.choices?.[0]?.message?.content ??
        (() => {
            throw new Error("OpenAI nevrátilo žádný obsah v message.content");
        })();

    let result;
    try {
        result = JSON.parse(content);
    } catch (e) {
        console.error("Odpovìï modelu není validní JSON:", content);
        throw e;
    }

    return result;
}

async function main() {
    // naèteme vstupy
    const [html, csText, systemPrompt] = await Promise.all([
        fs.readFile(htmlPath, "utf8"),
        fs.readFile(csPath, "utf8"),
        fs.readFile(promptPath, "utf8"),
    ]);

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

    // cs.json
    await fs.writeFile(
        csPath,
        JSON.stringify(result.cs, null, 2) + "\n",
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

        const langPath = path.join(langDir, `${lang}.json`);
        await fs.writeFile(
            langPath,
            JSON.stringify(data, null, 2) + "\n",
            "utf8",
        );
    }

    console.log("Lokalizace úspìšnì aktualizována.");
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
