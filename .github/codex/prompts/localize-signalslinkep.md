Jsi Codex a pracuješ v repozitáři Vintage Story módu SignalsLink.

Tvým úkolem při každém spuštění je:

1. Zpracovat vybrané soubory v adresáři `SignalsLink.EP/assets/signalslinkep/lang/` podle níže uvedených instrukcí. Budeš pracovat jen se soubory v tomto adresáři!!!
2. Přečíst HTML soubor `signalslinkep.html`.  
3. Převést jeho obsah na jeden řádek, bez znaků nového řádku (`\n` nebo `\r`).  
4. Zachovat HTML naprosto beze změny:
   - nesmíš měnit strukturu, tagy ani atributy,
   - nesmíš přidávat ani mazat mezery,  
   - nesmíš přidávat nějaké <br> nebo jiné tagy,
   - nesmíš zaměňovat znaky `\n` nebo `\r` za <br> tag,
   - nesmíš normalizovat HTML.
5. HTML escapuj tak, aby bylo možné vložit celé escapované HTML do JSON hodnoty. Zejména:
   - `</` → `<\/`
   - uvozovky a apostrofy escapuj podle JSON standardu,
6. Tento jednořádkový escapovaný HTML string vlož nebo aktualizuj v souboru `cs.json` pod klíčem:
   - `signalslinkep:usageinfo-signalslinkep-text`
7. `cs.json` je český zdrojový soubor. Všechny hodnoty v něm musí být česky a z nich se překládají ostatní jazyky. 
   Jediné co v něm smíš měnit, je hodnota klíče `signalslinkep:usageinfo-signalslinkep-text`, který aktualizuješ podle kroků 1-8.

Poté:

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
- U klíče `signalslinkep:usageinfo-signalslinkep-text`:
  - ponech HTML beze změny,
  - překládej pouze viditelný text mezi tagy,
  - výsledek musí být opět na jednom řádku,
  - escapuj `</` jako `<\/`,
  - nikdy nepřidávej `\n` nebo `\r`,
  - nikdy nepřidávej nějaké další tagy,
  - nikdy nezaměňuj znaky `\n` nebo `\r` za <br> tag.

Požadavky na formát JSON:

- soubory musí být uloženy jako UTF-8 bez BOM,
- odsazení 2 mezery,
- bez koncových čárek,
- hezky zformátované.

Rozsah povolených změn:

- smíš měnit pouze:
- `cs.json` zde pouze hodnotu klíče `signalslinkep:usageinfo-signalslinkep-text`,
- `de.json`
- `en.json`
- `es.json`
- `fr.json`
- `it.json`
- `pl.json`
- `pt.json`
- `ru.json`
- `sk.json`

- `signalslinkep.html` se používá jen jako vstupní soubor — nemáš ho měnit.

Před dokončením:

- ověř, že všechny změněné JSON soubory jsou syntakticky platné,
- ověř, že klíč `signalslinkep:usageinfo-signalslinkep-text` je přítomen ve všech jazykových souborech,
- ověř, že obsahuje jednovláknový (single-line) escapovaný HTML text v příslušném jazyce,
- ověř, že žádný z JSON souborů neobsahuje `\n` ani `\r` v hodnotě HTML klíče.
- ověř, že všechny soubory s přeloženými jazyky mají stejnou strukturu klíčů jako `cs.json` a obsahují text přeložený do příslušného jazyka.
