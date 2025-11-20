Jsi Codex a pracuješ v repozitáři Vintage Story módu SignalsLink.

Tvým úkolem při každém spuštění je:

1. Zpracovat vybrané soubory v adresáři `SignalsLink.EP/assets/signalslinkep/lang/` podle níže uvedených instrukcí. Budeš pracovat jen se soubory v tomto adresáři!!!
2. Přečíst HTML soubor `signalslinkep.html`.
3. Převést jeho obsah na jeden řádek, bez znaků nového řádku (`\n` nebo `\r`).

Pravidla pro HTML:

4. HTML strukturu, tagy a text musíš zachovat:
   - nesmíš měnit strukturu, tagy ani atributy,
   - nesmíš přidávat ani mazat mezery,
   - nesmíš přidávat žádné nové `<br>` ani jiné tagy,
   - nesmíš zaměňovat znaky `\n` nebo `\r` za `<br>` tag,
   - nesmíš normalizovat HTML (žádné přeformátování, žádné změny pořadí atributů).

5. Po převodu na jeden řádek HTML vlož do JSON hodnoty tak, aby JSON byl validní:
   - uvozovky v HTML atributech escapuj jako `\"` (standardní JSON escapování),
   - žádné jiné znaky nesmíš escapovat,
   - nesmíš přidávat zpětná lomítka navíc – výsledná hodnota nesmí obsahovat sekvence `\\` ani `\\\"`,
   - ponech `</` tak jak je, NEESCAPUJ ho na `<\/`,
   - výsledná hodnota musí být na jednom řádku (bez `\n` a `\r`).

Zápis do cs.json:

6. Tento jednořádkový escapovaný HTML string vlož nebo aktualizuj v souboru `cs.json` pod klíčem:
   - `signalslinkep:usageinfo-signalslinkep-text`

7. `cs.json` je český zdrojový soubor. Všechny hodnoty v něm musí být česky. Můžeš měnit pouze hodnotu klíče `signalslinkep:usageinfo-signalslinkep-text` kam zapíšeš to jednořádkové HTML, popsané v předchozích bodech.

Překlady:

8. Vygeneruj nebo aktualizuj jazykové JSON soubory tak, že přeložíš soubor `cs.json` do příslušného jazyka:
   - `de.json`
   - `en.json`
   - `es.json`
   - `fr.json`
   - `it.json`
   - `pl.json`
   - `pt.json`
   - `ru.json`
   - `sk.json`

Pravidla pro generování překladů:

- Klíče se **nikdy nepřekládají**, překládají se pouze hodnoty.
- Struktura a pořadí klíčů musí odpovídat `cs.json`.
  - Pokud jazykový soubor nemá nějaký klíč, přidej ho.
  - Pokud má nějaký navíc, odstraň ho.
- Překládej pouze text. Nepřekládej HTML tagy, atributy ani nic v závorkách `<>`.

Speciální pravidla pro klíč `signalslinkep:usageinfo-signalslinkep-text`:

- Použij HTML, které jsi připravil v krocích 2–5.
- Ponech HTML strukturu beze změny (tagy, atributy, pořadí `<br>` atd.).
- Překládej pouze viditelný text mezi tagy do daného jazyka.
- Výsledek musí být opět na jednom řádku.
- Uvozovky v atributech escapuj jako `\"`.
- Nesmíš produkovat žádnou sekvenci `\\` ani `\\\"` v hodnotě tohoto klíče.
- Nikdy nepřidávej `\n` nebo `\r`.
- Nikdy nepřidávej žádné nové tagy ani je nemaž.

Požadavky na formát JSON:

- soubory musí být uloženy jako UTF-8 bez BOM,
- odsazení 2 mezery,
- bez koncových čárek.

Rozsah povolených změn:

- smíš měnit pouze:
  - `cs.json`
  - `de.json`
  - `en.json`
  - `es.json`
  - `fr.json`
  - `it.json`
  - `pl.json`
  - `pt.json`
  - `ru.json`
  - `sk.json`


Před dokončením:

- ověř, že všechny změněné JSON soubory jsou syntakticky platné,
- ověř, že klíč `signalslinkep:usageinfo-signalslinkep-text` je přítomen ve všech jazykových souborech,
- ověř, že `cs.json` obsahuje jednovláknový (single-line) HTML text v češtině,
- ověř, že všechny JSON soubory obsahují jednovláknový HTML text v příslušném jazyce,
- ověř, že žádný z JSON souborů neobsahuje `\n` ani `\r` v hodnotě HTML klíče,
- ověř, že hodnoty klíče `signalslinkep:usageinfo-signalslinkep-text` **neobsahují žádnou sekvenci `\\` ani `\\\"`**,
- ověř, že všechny soubory s přeloženými jazyky mají stejnou strukturu klíčů jako `cs.json` a obsahují text přeložený do příslušného jazyka.
