import fs from "fs/promises";
import path from "node:path";
import OpenAI from "openai";

const openai = new OpenAI({
  apiKey: process.env.OPENAI_API_KEY,
});

// Cesty pøizpùsob svému projektu
const langDir = path.resolve("SignalsLink.EP/assets/signalslinkep/lang");
const htmlPath = path.join(langDir, "signalslinkep.html");
const csPath = path.join(langDir, "cs.json");

// prompt soubor
const promptPath = path.resolve(".github/prompts/localize-signalslinkep.md");

// Jazyky
const targetLangs = ["de", "en", "es", "fr", "it", "pl", "pt", "ru", "sk"];

async function main() {
  const [html, csText, systemPrompt] = await Promise.all([
    fs.readFile(htmlPath, "utf8"),
    fs.readFile(csPath, "utf8"),
    fs.readFile(promptPath, "utf8"),
  ]);

  const cs = JSON.parse(csText);

  const userPayload = {
    html,
    cs,
    targetLanguages: targetLangs,
  };

  const completion = await openai.chat.completions.create({
    model: "gpt-5-mini",
    response_format: { type: "json_object" },
    messages: [
      { role: "system", content: systemPrompt },
      {
        role: "user",
        content: JSON.stringify(userPayload),
      },
    ],
  });

  const content = completion.choices[0]?.message?.content;
  if (!content) {
    throw new Error("Model nevrátil žádný obsah");
  }

  let result;
  try {
    result = JSON.parse(content);
  } catch (e) {
    console.error("Nepodaøilo se parse-nout JSON z odpovìdi:", content);
    throw e;
  }

  // cs.json
  if (!result.cs) {
    throw new Error("Výsledek neobsahuje klíè 'cs'");
  }

  await fs.writeFile(
    csPath,
    JSON.stringify(result.cs, null, 2) + "\n",
    "utf8"
  );

  // ostatní jazyky
  for (const lang of targetLangs) {
    const data = result[lang];
    if (!data || typeof data !== "object") {
      console.warn(`Varování: výsledek pro jazyk '${lang}' chybí nebo není objekt, pøeskoèeno.`);
      continue;
    }

    const langPath = path.join(langDir, `${lang}.json`);
    await fs.writeFile(
      langPath,
      JSON.stringify(data, null, 2) + "\n",
      "utf8"
    );
  }

  console.log("Lokalizace úspìšnì aktualizována.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
