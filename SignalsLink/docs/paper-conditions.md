# Podmínky na papíře (Paper Conditions)

Podmínky na papíře jsou **sdílený systém**, kterým se pravidly napsanými na kus papíru řídí prvky modu SignalsLink. Pravidla napíšeš na papír a papír vložíš do podporovaného zařízení.

**Používají je:**

- **ManagedChute** — řízený žlab (přenos předmětů);
- **ManagedHose** — řízená hadice / ventil (přenos kapalin);
- **senzory** (např. BlockSensor) — výstup signálu podle sledovaného bloku.

Všechna tato zařízení sdílí **stejnou syntaxi podmínek i stejné vyhodnocovací pravidlo** ([Model vyhodnocení bloků](#model-vyhodnocení-bloků)). Liší se jen v tom, co je pro ně **zdroj** a **cíl**, jaká je jejich **defaultní akce** a které **direktivy/akce** podporují — viz [Odlišnosti podle zařízení](#odlišnosti-podle-zařízení).

## Základy

Blok podmínek tvoří jeden odstavec. Jednotlivé bloky oddělte jedním nebo více prázdnými řádky.

```text
podmínka A
podmínka B
instrukce

podmínka C
instrukce
```

V rámci jednoho bloku musí být splněny **všechny podmínky** (mezi podmínkami platí logické AND). Bloky se vyhodnocují **shora dolů** a provede se **první platný blok** — přesná pravidla viz [Model vyhodnocení bloků](#model-vyhodnocení-bloků).

Komentáře a prázdné řádky uvnitř bloku se ignorují:

```text
# Přesouvej pouze silný tanin
*strongtannin*

// Toto je také komentář
```

Pro komentář použijte na začátku řádku `#` nebo `//`.

## Model vyhodnocení bloků

Vyhodnocení má **jedno jednoduché pravidlo, které platí všude stejně** — pro ManagedChute, ManagedHose i senzory. Liší se jen **defaultní akce** dané třídy, ne pravidlo samotné.

**Blok = podmínky + právě jedna akce.** Bloky se procházejí **shora dolů** a provede se **první _platný_ blok**. Blok je platný, když jsou splněny **všechny jeho podmínky** (AND) **a jeho akci lze fyzicky provést**.

### Akce bloku

Každý blok má právě jednu akci:

- Blok **bez** explicitně uvedené akce → provede **defaultní akci své třídy**, tvarovanou direktivami (`target`, `amount`, `ifEmpty`).
- Blok s **explicitní akcí** (`output X`, `do seal`) → tuto akci provede a defaultní akci tím **nahradí** (takže např. blok s `output X` ani blok s `do seal` **nic nepřenáší**).

| Třída | Defaultní akce | `output X` | `do seal` |
|---|---|---|---|
| ManagedChute | přenos předmětů | — (nemá výstupní pin) | ✅ |
| ManagedHose (ventil) | přenos kapalin | ✅ | ✅ |
| Senzory | výstup signálu | ✅ (to je jeho výstup) | — |

Explicitní akci musí daná třída podporovat: `output` má smysl jen tam, kde je výstupní pin (ManagedHose, senzory — **ne** ManagedChute); `do seal` jen tam, kde se pracuje se sudem (ManagedChute, ManagedHose — **ne** senzory).

### Fyzická platnost bloku

Platnost nezáleží jen na podmínkách, ale i na tom, zda **akce bloku reálně proběhne**:

- **Přenos** je platný, jen když opravdu něco přesune — zdroj má co odebrat a cíl má kam vložit (a je-li uvedeno `target N ifEmpty`, je cílový slot prázdný). Když by přenos nic nepřesunul (zdroj prázdný / cíl plný / `ifEmpty` neplatí), je **blok neplatný** a vyhodnocení **pokračuje dalším blokem**.
- **`output X`** je platný, jen když výstupní pin **reálně změní**. Pokud už pin hodnotu X má, akce nic neudělá → **blok je neplatný** a vyhodnocení **pokračuje dalším blokem** (stejně jako u přenosu, který nic nepřesune).
- **`do seal`** je platný, když je co zapečetit.

Díky tomu lze bloky skládat do sekvence: horní bloky plní, a jakmile „dojedou" (nemají co přenášet nebo už neplatí jejich `ifEmpty`), spadne vyhodnocení na další blok.

### Jak často

Bloky se vyhodnocují **opakovaně** — ManagedChute i ManagedHose je procházejí při každém svém pracovním tiku, dokud jsou aktivní (u ManagedHose navíc jen když ventil právě drží token střídání). Dokud je vybraný blok platný a má co přenášet, přenos pokračuje; jakmile přestane platit, nastupuje další blok.

U ManagedHose platí ještě: ventil, jehož podmínky obsahují `output`, vyhodnocuje **každý tik**, aby výstup reagoval okamžitě (např. hned spadl na 0, když se zdroj vyprázdní). Čistě přenášecí ventil, který zrovna nemá co dělat (prázdný zdroj / plný cíl), může frekvenci vyhodnocování dočasně snížit kvůli výkonu a vrátí se na plný takt, jakmile je zase co přenášet.

### Výstupní pin drží hodnotu

Kde má zařízení výstupní pin, **drží poslední nastavenou hodnotu**, dokud ji nepřepíše jiný platný blok s `output X`. Když žádný `output` blok neplatí, hodnota se nemění.

## Odlišnosti podle zařízení

Pravidlo je stejné pro všechny, ale **zdroj**, **cíl**, defaultní akce a podporované direktivy/akce se liší:

### ManagedChute (žlab)

- **Přenáší předměty.** Zdroj = inventář bloku na vstupní straně, cíl = inventář bloku na výstupní straně.
- Defaultní akce: **přenos předmětů**.
- Podporuje direktivy `target` / `amount` / `ifEmpty` a akci `do seal`.
- **Nemá výstupní pin → `output` nepodporuje.**
- Zdroj prochází po slotech (podle signálu zdrojového slotu) a hledá kandidátní předmět.

### ManagedHose (ventil)

- **Přenáší kapaliny.** Zdroj = vzdálený konec hadice (hostitel protějšího ventilu, nebo **Sání** = voda ve světě), cíl = **vlastní** hostitelský blok ventilu.
- Defaultní akce: **přenos kapalin**.
- Podporuje `target` / `amount` / `ifEmpty`, `do seal` **i `output`** (má výstupní pin, hodnoty 0–15).
- Kapalinová specifika: **lávu nepřenáší** a **horkou vodu v cíli ochladí** na okolní teplotu.
- Vyhodnocuje jen když ventil zrovna drží **token střídání** (dva protilehlé ventily se ve čerpání střídají).

### Senzory (např. BlockSensor)

- **Nic nepřenáší — dává signál na výstup.** „Zdroj" = sledovaný blok / jeho inventář; **cíl ve smyslu přenosu neexistuje**.
- Defaultní akce: **výstup signálu** (bez `output` vrací výchozí hodnotu — např. úroveň zaplnění nebo číslo slotu).
- Podporuje `output` (včetně `output .` = číslo shodného slotu).
- **Přenosové direktivy (`target` / `amount` / `ifEmpty`) ani `do seal` nedávají smysl** — senzor nepřenáší.

## Co se vyhodnocuje ve výchozím stavu

Pokud rozsah nezmění direktiva, podmínky se vztahují na **zdroj**:

- u žlabu / hadice na **kandidátní předmět/kapalinu ve zdroji** (zařízení prochází zdroj a hledá první vyhovující);
- u senzoru na **sledovaný blok / jeho inventář**.

Například:

```text
game:resin
```

U žlabu bude pro přesun vybrána pouze pryskyřice.

## Podmínky kódu

### Přesný kód

Použijte úplný kód předmětu nebo bloku:

```text
game:resin
game:log-placed-oak-ud
```

### Zástupné znaky

- `*` odpovídá libovolnému počtu znaků.
- `?` odpovídá právě jednomu znaku.

Příklady:

```text
game:log-placed-*
*strongtannin*
game:ingot-?
```

### Regulární výraz

Regulární výraz uveďte s předponou `@`:

```text
@^game:log-placed-(oak|maple)-.*$
```

## Porovnávání kódů kapalin

Obsah kapalin se interně ukládá jako položky představující část kapaliny, například `game:waterportion`. Podmínky kódu ale kontrolují také kódy rozlitého/světového bloku kapaliny z jejích metadat.

Vzory určené pro světové kódy kapalin proto fungují i pro obsah kapalin v sudech, hrncích, vědrech, kotlích a dalších kapalných inventářích:

```text
game:water-*
game:water-still-7
*weaktannin*
```

Podmínka může odpovídat také přímo kódu položky části kapaliny:

```text
game:waterportion
```

Platí to pro běžné podmínky kódu i pro podmínky inventáře.

## Negace

Podmínku znegujete předponou `!`:

```text
!game:resin
```

Příklad: přesuň každý vybraný zdrojový předmět kromě pryskyřice.

```text
!game:resin
target 2
```

## Atributy předmětů a kontextové hodnoty

Samotný platný název atributu ověřuje, zda atribut existuje a je pravdivý, případně nenulový:

```text
isBaked
```

Porovnání podporují operátory `>`, `>=`, `<`, `<=`, `=` a `==`:

```text
temperature>1100
durabilityRatio>=0.75
stackSize>=16
isSpoiling=true
```

Dostupné generované kontextové hodnoty:

| Hodnota | Význam |
|---|---|
| `stackSize` | Velikost vyhodnocovaného stacku. |
| `temperature` | Teplota předmětu, pokud ji předmět podporuje. |
| `durability` | Aktuální odolnost, pokud se používá. |
| `durabilityMax` | Maximální odolnost, pokud se používá. |
| `durabilityRatio` | Aktuální odolnost dělená maximální odolností. |
| `freshHoursLeft` | Zbývající čas čerstvosti, má-li předmět data přechodu. |
| `isSpoiling` | Pravda, pokud `freshHoursLeft <= 0`. |

Porovnávat lze také vlastní číselné a logické atributy stacku předmětu.

## `inventoryAny`

`inventoryAny <podmínka>` ověří, zda **libovolný slot** aktuálně zvoleného inventáře obsahuje předmět odpovídající vnořené podmínce.

```text
inventoryAny game:resin
```

Příklad: předmět se přesune jen tehdy, pokud zdrojový inventář již někde obsahuje pryskyřici:

```text
game:log-placed-oak-ud
inventoryAny game:resin
```

`inventoryAny` kontroluje také obsah kapaliny ve slotech kapalných inventářů. Dokáže například rozpoznat vodu uloženou v kapalném slotu sudu:

```text
inventoryAny game:water-*
```

## Podmínky množství v inventáři

Podmínka množství sestává ze vzoru kódu a množství:

```text
<vzor> <množství>
```

Kontroluje celkové množství odpovídajícího obsahu v inventáři aktuálního rozsahu.

```text
game:log-placed-oak-ud 5
game:water-still-7 50
```

Ve výchozím stavu musí množství odpovídat **přesně**.

Přidejte `+` pro alespoň dané množství:

```text
game:resin 10+
```

Přidejte `-` pro nejvýše dané množství:

```text
game:resin 10-
```

U běžných předmětů je množství počet kusů. U kapalin je množství v litrech, vypočtené z metadat kapaliny `itemsPerLitre` a zaokrouhlené dolů na dvě desetinná místa.

Příklady:

```text
# Přesně pět dubových klád a přesně padesát litrů vody
game:log-placed-oak-ud 5
game:water-* 50

# Alespoň 20 litrů slabého taninu
*weaktannin* 20+
```

## Direktivy rozsahu: `in source` a `in target`

Direktiva rozsahu mění inventář vyhodnocovaný následujícími podmínkami ve stejném bloku.

```text
in source
in target
```

- `in source` je výchozí nastavení. Označuje inventář, z něhož žlab odebírá předměty.
- `in target` označuje inventář, do něhož žlab předměty vkládá.

Pro běžné podmínky kódu, zástupných znaků, regulárních výrazů, atributů a `inventoryAny` platí:

- podmínka v rozsahu zdroje filtruje právě vybraný zdrojový předmět;
- podmínka v rozsahu cíle vyhledává odpovídající předmět nebo obsah kapaliny v cílovém inventáři.

Podmínky množství počítají celý inventář ve zvoleném rozsahu.

Příklad: přesuň pryskyřici pouze tehdy, pokud cílový inventář již obsahuje alespoň deset předmětů odpovídajících `game:resin`.

```text
game:resin
in target
game:resin 10+
```

V případě potřeby se přepněte zpět na zdrojový rozsah:

```text
in source
game:resin
in target
game:water-* 20+
in source
stackSize>=4
```

## Direktivy přesunu

Direktivy tvarují **defaultní přenos** — platí tedy pro **ManagedChute a ManagedHose**, u senzorů nemají smysl. Nejsou to samy o sobě podmínky.

### `target <slot>`

Nahradí signál cílového slotu žlabu pro odpovídající blok. Sloty jsou číslované **od jedné**.

```text
game:resin
target 4
```

Tím se odpovídající pryskyřice odešle do slotu 4 cílového inventáře.

### `target <slot> ifEmpty`

Přidá k bloku požadavek, že cílový slot musí být **prázdný**. `ifEmpty` je **součást platnosti bloku**: dokud je slot prázdný, blok může přenášet; jakmile se naplní, **blok přestane být platný a vyhodnocení pokračuje dalším blokem**.

To umožňuje plnit více slotů po sobě:

```text
*water*
target 4 ifEmpty
amount 2

*water*
target 6 ifEmpty
amount 3
```

Dokud je slot 4 prázdný, platí první blok a nalijí se do něj 2. Jakmile slot 4 prázdný není, první blok přestane platit a nastupuje druhý — nalije 3 do slotu 6. Poté už žádný blok neplatí a nic dalšího se nepřenese.

U varných slotů ohniště / EP sporáku není neaktivní varný slot použitelný; musí v něm být hrnec nebo jiná varná nádoba.

### `amount <množství>`

Nahradí běžné přenášené množství pro odpovídající blok.

```text
game:resin
amount 3
```

`amount` je **velikost jedné dávky** — kolik se přesune při jedné odpovídající operaci. Vstupní signál zařízení přitom řídí **celkové** množství k přenosu: signál 1–7 naplní *buffer* v kusech/litrech, který se každým přenosem sníží o **skutečně přenesené množství** (ne o 1). `amount` tedy jen určuje, po jak velkých dávkách se buffer čerpá; buffer sám říká, kolik ještě zbývá přenést. (Signál `15` = přenášej průběžně bez omezení.)

Množství může být desetinné s tečkou jako oddělovačem:

```text
game:water-*
amount 2.75
```

U kapalin představuje hodnota litry a podporuje maximálně dvě desetinná místa. U běžných předmětů se desetinná část zahodí; pokud je zadané množství větší než nula, výsledkem je alespoň jeden kus.

Chování na konci/okrajích se liší podle média:

- **ManagedChute (předměty):** dávka je **atomická** — žlab spojí odpovídající stejné stacky z více zdrojových slotů a `amount` přenese jen tehdy, když je celé množství ve zdroji k dispozici; jinak se nespustí. Poslední naplnění dávky se dokončí celé (buffer může přetéct o méně než `amount`).
- **ManagedHose (kapaliny):** přenese se **až** `amount` — kolik zdroj má, cíl pojme a zbývající buffer dovolí (klidně i méně). Např. buffer 3 a `amount 6` → přeteče jen 3.

## `output` — akce nastavení výstupu

`output X` je **akce**, ne přílepek k přenosu: platný blok s `output X` nastaví **výstupní pin** zařízení na hodnotu X a **defaultní akci (přenos) tím nahradí** — takový blok nic nepřenáší. Přípustné hodnoty jsou **0 až 15**.

```text
in target
*leather* 5+
*water* 30
output 5
```

Blok nastaví výstup na 5, jakmile je v cíli aspoň 5 kůží a 30 vody. Výstup ovládaný podmínkou zapiš na vhodné místo (typicky **za** přenosové bloky): dokud přenosové bloky nad ním pracují, jsou platné a output blok se ke slovu nedostane; jakmile „dojedou" a podmínky output bloku platí, výstup se nastaví.

Výstupní pin **drží poslední hodnotu**, dokud ji nepřepíše jiný platný `output` blok.

### `output X` propadne, když nic nezmění

Protože `output X` je platný jen když pin **reálně změní** (viz [Fyzická platnost bloku](#fyzická-platnost-bloku)), blok s `output X`, jehož hodnota už na pinu je, **nic neudělá a vyhodnocení propadne na další blok**. Díky tomu můžeš dát `output` i jako **první** blok — reset dřívějšího signálu **před** tím, než začneš plnit:

```text
# 1) když je cíl prázdný, shoď dřívější "plno" signál na 0
in target
*water* 0
output 0

# 2) plň cíl vodou
game:water-*
target 2 ifEmpty
amount 6
```

Když je sud prázdný a pin je na 5, první blok pin změní na 0 (vyhraje). Další tik už je pin na 0 → první blok propadne a spustí se plnění. Kdyby `output X` platil „vždy", zůstal by první blok navěky platný a k plnění pod ním by se nikdy nedošlo.

> `output` má smysl jen u zařízení s výstupním pinem — **ManagedHose (ventil)** a **senzory**. **ManagedChute výstupní pin nemá**, takže u něj `output` nedává smysl.

### `output .` (jen senzory)

U senzorů tečka místo čísla znamená „vrať pořadí/slot, ve kterém došlo ke shodě" (typicky číslo slotu). U ManagedChute/ManagedHose se nepoužívá.

```text
game:resin
output .
```

## `do seal` — akce zapečetění sudu

`do seal` je **akce**: platný blok s `do seal` **zapečetí cílový sud** a defaultní akci (přenos) tím **nahradí** — blok s `do seal` nic nepřenáší, jen pečetí.

```text
in target
game:water-* 50+
do seal
```

Příklad zapečetí cílový sud, jakmile obsahuje aspoň 50 litrů vody. Blok je platný, když platí jeho podmínky a sud lze zapečetit; typicky ho dáš **za** přenosové bloky, aby se k němu vyhodnocení dostalo, až když je náplň hotová.

Zapečetěný sud už `do seal` znovu nezapečetí. `do seal` má smysl jen u ManagedChute/ManagedHose (ne u senzorů) a jen když je v cílové pozici sud.

## Úplné příklady

### Přesuň dřevěné kmeny do konkrétního slotu

```text
game:log-placed-*
target 3
```

### Přesuň jen velké stacky pryskyřice

```text
game:resin
stackSize>=16
amount 8
```

### Naplň prázdný slot přesným množstvím

```text
game:resin
target 2 ifEmpty
amount 12
```

### Přesuň vodu, jen pokud cíl již obsahuje tanin

```text
game:water-*
in target
inventoryAny *tannin*
in source
amount 5
```

### Přesuň položku pouze tehdy, když zdroj obsahuje přesný počet

```text
game:log-placed-oak-ud
game:log-placed-oak-ud 10
amount 10
```

### Zapečeť sud po naplnění

```text
in target
game:water-* 50+
do seal
```

Blok s `do seal` nepřenáší — zapečetí, jakmile je v cíli aspoň 50 vody.

### Naplň sud a pak zapečeť (sekvence bloků)

```text
# 1) plň, dokud je slot 2 prázdný
game:water-*
target 2 ifEmpty
amount 50

# 2) až je naplněno, zapečeť
in target
game:water-* 50+
do seal
```

Dokud je slot 2 prázdný, platí první blok a lije vodu. Jakmile se naplní, první blok přestane platit (`ifEmpty`) a vyhodnocení spadne na druhý blok, který sud zapečetí.

### Signalizuj na výstup po naplnění (jen ManagedHose / senzory)

```text
# 1) plň
game:water-*
target 2 ifEmpty
amount 30

# 2) až je hotovo, vystav feedback na výstupní pin
in target
*leather* 5+
game:water-* 30+
output 5
```

Po naplnění (30 vody a aspoň 5 kůže v cíli) přestane platit přenosový blok a druhý blok nastaví výstupní pin na 5 — třeba pro spuštění další hadice přes Signals.

## Omezení a důležité poznámky

- Platí **jedno pravidlo**: shora dolů se provede **první platný blok** — mají-li být splněny všechny jeho podmínky (AND) a jeho akce musí jít fyzicky provést. Žádné bloky se nevyhodnocují „zvlášť"; přenos, `output` i `do seal` jsou jen různé **akce** téhož pravidla.
- Blok, který má provést **přenos** (defaultní akce žlabu/hadice), musí obsahovat aspoň jednu podmínku v rozsahu `in source` (musí umět vybrat, co přenáší). Blok s explicitní akcí (`output`, `do seal`) tuto podmínku mít nemusí.
- `target`, `amount`, `ifEmpty` jsou **direktivy** — tvarují defaultní přenos, nejsou to samy o sobě podmínky.
- `output` a `do seal` jsou **akce** — nahrazují defaultní akci bloku.
- `output X` je platný, jen když výstupní pin **reálně změní**; když už hodnotu X má, blok propadne na další (umožňuje dát reset `output 0` nad plnicí bloky).
- `amount` je velikost jedné dávky; vstupní signál 1–7 naplní buffer v kusech/litrech, který se čerpá o skutečně přenesené množství (ne o počet dávek). Signál `15` = průběžně.
- `do seal` má smysl jen tam, kde je v cílové pozici sud; `output` jen tam, kde má zařízení výstupní pin (ManagedHose, senzory — ne ManagedChute).
- Množství kapalin se vyhodnocuje v litrech, zatímco `stackSize` u kapaliny je interní počet porcí.
- U podmínek množství používej tečku jako desetinný oddělovač, například `2.75`.
