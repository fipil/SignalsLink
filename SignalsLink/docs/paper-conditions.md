# Podmínky na papíře (Paper Conditions)

Podmínky na papíře jsou **sdílený systém**, kterým se pravidly napsanými na kus papíru řídí prvky modu SignalsLink. Pravidla napíšeš na papír a papír vložíš do podporovaného zařízení.

**Používají je:**

- **ManagedChute** — řízený žlab (přenos předmětů);
- **ManagedSleeve** — řízený rukáv / klapka (přenos předmětů na dálku);
- **ManagedHose** — řízená hadice / ventil (přenos kapalin);
- **senzory** (např. BlockSensor) — výstup signálu podle sledovaného bloku.

Všechna tato zařízení běží na **jednom společném jádře**: stejná syntaxe, stejný průchod, stejný význam výstupního pinu ([Model vyhodnocení bloků](#model-vyhodnocení-bloků)). Liší se jen v tom, co je pro ně **zdroj** a **cíl**, jaká je jejich **defaultní akce** a které **direktivy/akce** podporují — viz [Odlišnosti podle zařízení](#odlišnosti-podle-zařízení).

## Základy

Blok podmínek tvoří jeden odstavec. Jednotlivé bloky oddělte jedním nebo více prázdnými řádky.

```text
podmínka A
podmínka B
instrukce

podmínka C
instrukce
```

V rámci jednoho bloku musí být splněny **všechny podmínky** (mezi podmínkami platí logické AND). Bloky se vyhodnocují **shora dolů** — přesná pravidla viz [Model vyhodnocení bloků](#model-vyhodnocení-bloků).

> **Pozor na AND.** Dvě podmínky na stejný kód se nepřebíjejí, musí platit obě. To je užitečné — `game:planks 5+` a `game:planks 20-` v jednom bloku znamená „mezi pěti a dvaceti". Ale znamená to taky, že **jediný nesplnitelný řádek zabije celý blok**. Klasika je `game:planks *` s mezerou: vypadá to jako „prkna, jakékoli množství", ale je to vzor pro kód, který obsahuje mezeru — a ten neexistuje. Na tohle papír upozorní, viz [Chyby v papíru](#chyby-v-papíru).

Komentáře a prázdné řádky uvnitř bloku se ignorují:

```text
# Přesouvej pouze silný tanin
*strongtannin*

// Toto je také komentář
```

Pro komentář použijte na začátku řádku `#` nebo `//`.

## Model vyhodnocení bloků

Jeden průchod papírem odshora dolů, který nese **dvě koleje najednou**:

- **Output kolej** — určí hodnotu výstupního pinu.
- **Akční kolej** — provede akci (přenos, `do seal`).

Jeden průchod tedy může **zároveň** nastavit výstup i něco přenést. Nejsou to dva oddělené průchody: bloky se procházejí **v pořadí, jak jsou napsané**, a každý se zařadí do jedné z kolejí podle toho, jestli má direktivu `output`.

- **Output blok** — blok s direktivou `output`. Nic nepřenáší.
- **Akční blok** — blok bez `output`. Provádí defaultní akci své třídy, tvarovanou direktivami (`target`, `amount`, `ifEmpty`), nebo explicitní akci `do seal`.

### Tři pravidla

**Vyhrává první platný output blok.** Jakmile nějaký platí, další output bloky se přeskočí.

**Když neplatí žádný output blok, pin je 0.** Pin je **obraz aktuálního stavu**, ne paměť poslední změny. Papír bez jediného `output` bloku znamená pin trvale na nule.

**Akční kolej provede jednu akci za průchod** — první blok, jehož akce opravdu odvede práci. Blok, jehož přenos nic nepřesune (prázdný zdroj, plný cíl, neplatné `ifEmpty`), **propadne na další blok**. Na tom stojí řetěz `target N ifEmpty` bloků, které plní slot za slotem.

### Na pořadí záleží

Protože jde o jeden průchod, **záleží na tom, kde output blok stojí vůči akčnímu**. Akce mění stav cíle, takže tentýž output blok nad akcí a pod akcí odpoví v témž tiku jinak. Je to na tobě, kam ho napíšeš.

Stejně tak **pořadí bloků poráží pořadí slotů**: blok výš na papíře prohledá všechny sloty, které smí, a teprve pak pustí ke slovu blok pod sebou. O prioritě rozhoduje papír, ne rozložení truhly.

### Jak často

Průchod běží **při každém pracovním tiku** zařízení, a to **nezávisle** na vstupním signálu, na tokenu střídání i na zpomalení při nečinnosti. Tyhle tři věci zavírají jen **akční kolej** — pin se počítá dál.

Díky tomu se zařízení s `output` blokem chová jako senzor: hlásí stav svého konce, i když zrovna nemá kredit, nemá co přenášet nebo čeká, až na něj přijde řada.

Jediná výjimka je výkonová: když na papíře **není žádný output blok** a zároveň jsou akce zablokované, průchod se přeskočí. Není co dělat ani co počítat.

### Akce podle tříd

| Třída | Defaultní akce | výstupní pin | `do seal` |
|---|---|---|---|
| ManagedChute (žlab) | přenos předmětů | ✅ | ✅ |
| ManagedSleeve (klapka) | přenos předmětů | ✅ | ✅ |
| ManagedHose (ventil) | přenos kapalin | ✅ | ✅ |
| Senzory | výstup signálu | ✅ (to je jeho výstup) | — |

Klapka nemá piny Zdroj a Cíl, takže na ní **nejsou dostupné signálové režimy 1–14 pro výběr slotu ani režimy Cíle „polož blok / polož nádobu na zem"**. Výběr slotů se dělá direktivami `source N` / `target N` a režim na zem výhradně direktivou `target ground`.

## Odlišnosti podle zařízení

Pravidlo je stejné pro všechny, ale **zdroj**, **cíl**, defaultní akce a podporované direktivy/akce se liší:

### ManagedChute (žlab)

- **Přenáší předměty.** Zdroj = inventář bloku na vstupní straně, cíl = inventář bloku na výstupní straně.
- Defaultní akce: **přenos předmětů**.
- Podporuje direktivy `source` / `target` / `amount` / `ifEmpty` a akci `do seal`.
- **Má výstupní pin** (kotva Output), takže `output` podporuje stejně jako ostatní. Už postavené žlaby ve starých světech novou kotvu dostanou samy při načtení.
- Zdroj prochází po slotech (podle signálu zdrojového slotu) a hledá kandidátní předmět.

### ManagedHose (ventil)

- **Přenáší kapaliny.** Zdroj = vzdálený konec hadice (hostitel protějšího ventilu, nebo **Sání** = voda ve světě), cíl = **vlastní** hostitelský blok ventilu.
- Defaultní akce: **přenos kapalin**.
- Podporuje `source` / `target` / `amount` / `ifEmpty`, `do seal` **i `output`** (výstupní pin, hodnoty 0–15).
- `source N` u ventilu vybírá slot kapaliny na vzdáleném konci hadice.
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
| `isLiquidContainer` | Pravda pro vědro a jinou přenositelnou nádobu na kapalinu. |
| `liquidContainerEmpty` | Pravda, pokud nádoba neobsahuje kapalinu. |
| `liquidContainerFilled` | Pravda, pokud nádoba obsahuje kapalinu. |
| `liquidCode` | Kód kapaliny v nádobě, například `game:waterportion`. Textový kód porovnávejte přes `=` nebo `==`. |
| `liquidLitres` | Množství kapaliny v litrech. |

Porovnávat lze také vlastní číselné a logické atributy stacku předmětu.

Například plné vědro s deseti litry vody vyberete takto:

```text
isLiquidContainer
liquidContainerFilled
liquidCode=game:waterportion
liquidLitres=10
```

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

### Množství v konkrétním slotu: `slot N`

Za množství můžeš připsat `slot N` a ptát se jen na **jeden slot** místo na celý inventář. Sloty
se počítají **od jedničky**, stejně jako u `target N` a `source N`.

```text
in target
game:firewood 96+ slot 2
```

Slot mimo rozsah inventáře se čte jako **prázdný**, ne jako „nevím" — inventář ten slot prostě
nemá a poctivá odpověď na „kolik je v něm" je nula.

Rozdíl proti dotazu na celý inventář je podstatný:

```text
# dva sloty po padesáti: dohromady sto, ale ani v jednom není 96
in target
game:firewood 96+          # platí
game:firewood 96+ slot 1   # neplatí
```

**Podmínka se slotem je hlídka, ne výběr.** Ptá se na inventář, ne na nesený předmět, takže:

- neúčastní se pravidla „kandidátní předmět musí sedět na kód" (viz níže),
- a **sama o sobě blok nekvalifikuje k přenosu** — kdyby v bloku stála jediná, nebylo by podle
  čeho vybrat, co se má nést. Takový blok je neplatný.

Díky tomu jde jedním blokem přenášet jednu věc a podmiňovat to jinou:

```text
# ber polena, ale jen dokud je ve slotu 5 aspoň deset prken
game:firewood 23+
game:plank 10+ slot 5
target 1
```

Bez `slot 5` by ten druhý řádek žádal, aby nesený předmět byl zároveň poleno i prkno — a blok by
nikdy neplatil.

Nejvíc se to hodí v rozsahu `in target`, kde se dá podle obsahu konkrétních slotů poznat, **v jaké
fázi** vícekrokového postupu zařízení právě je (co se zrovna vaří v hrnci, jestli už je hotový
meziprodukt odebraný, a tak dál).

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

Direktivy tvarují **defaultní přenos** — platí tedy pro **ManagedChute a ManagedHose**, u senzorů nemají smysl. Nejsou to samy o sobě podmínky. Direktiva `source` platí pouze pro ManagedChute.

### `source <slot>`

Pouze pro ManagedChute. Určí zdrojový slot, číslovaný **od jedné**, a pro odpovídající blok má přednost před signálem na pinu Zdroj. Podporovány jsou sloty 1 až 14.

```text
*carrot*
source 3
target 6 ifEmpty
```

Příklad odešle mrkev ze zdrojového slotu 3 do cílového slotu 6, i kdyby pin Zdroj ukazoval jinam. Bloky bez `source` nadále používají pin Zdroj jako dosud.

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

### `target firepit`

Postaví na cíli ohniště místo toho, aby do něj něco vkládala. Stavba jde po stupních přesně jako u hráče a materiál se čte z drop tabulky vanilla ohniště: **1× suchá tráva** založí `construct1`, další **4× polena** posunou `construct2` → `construct3` → `construct4` → hotové ohniště.

Direktiva znamená „tenhle materiál patří ohništi na cíli": dokud se staví, posouvá stupeň, a jakmile ohniště stojí, přiloží do něj palivo. Jeden pokus o přenos = jeden stupeň.

```text
game:drygrass
target firepit
---
game:firewood
target firepit
```

**O pořadí se starat nemusíš.** Blok se vybírá podle toho, co cíl zrovna potřebuje: na prázdné zemi projde jen tráva, na rozestavěném ohništi jen polena. Stačí mít ve zdroji obojí.

Je to **vlastní direktiva schválně**: shodit trávu na zem přes `target ground` je normální věc a nemá se z ní potichu stávat stavba.

> Milíř nevzniká zapálením hromady polen, ale z ohniště postaveného na odkrytém stacku v utěsněné jámě. Proto ten krok jde automatizovat právě takhle.

### `isBurning`

Platí pro **blok**, ne pro předmět — ptá se, jestli to, co je na dané pozici, právě hoří. Ve scope `in target` se ptá na cílový blok, jinak na zdrojový; když daná třída pro ten scope pozici nezná, podmínka je nepravdivá (nikdy pravdivá naslepo).

```text
in target
isBurning
output 3
```

Funguje i s vykřičníkem: `!isBurning`.

Hoření se nezjišťuje podle seznamu typů, ale podle toho, jestli block entity (nebo některý její behavior) vystavuje `IsBurning` / `Lit`. Chytne tedy ohniště, hromádku na zemi, hromadu uhlí i milíř — a bez úprav i bloky z jiných modů, které to takhle pojmenovaly. Samotný blok ohně se počítá jako hořící vždycky.

### `target ground` / `target ground N`

Platí pro ManagedChute, když její cíl míří do vzduchu. Ignoruje signál na pinu Cíl a pokusí se položit vybraný blok, vědro včetně obsahu nebo položku, která umí vytvořit hromádku na zemi. Pokud na cíli není pevná zem nebo položku nelze umístit, blok podmínek neprovede přenos a může propadnout na další blok.

```text
isLiquidContainer
liquidContainerFilled
target ground
```

**Hromádky skládatelných předmětů.** Polena, ingoty, desky a další předměty, které jde na zemi hromadit, se ukládají do hromádek — žlab hromádku i sám založí na prázdném místě. Kapacita hromádky je vlastnost předmětu, ne velikost stacku: hromádka ingotů pojme 64 kusů, i když se ingoty stackují po 16.

`target ground N` nechá sloupec **růst nahoru**: jakmile je hromádka plná, pokračuje se o blok výš, až do výšky N bloků nad cílovou pozicí. Sloupec se zastaví na pevném bloku nebo když pod novou hromádkou není opora. Bez čísla se plní jen cílový blok (`target ground` = `target ground 1`).

```text
# sloupec polen až deset bloků vysoký
game:firewood
target ground 10
amount 32
```

**Sbírání** ze země je obrácené: bere se z **vrcholu** sloupce a vyprázdněné hromádky se odstraňují, takže se sloupec rozebírá odshora dolů. Nepotřebuje žádnou direktivu — stačí, aby žlab mířil zdrojovou stranou na spodní hromádku sloupce.

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
- **Hromádky na zemi (`target ground`):** dávka je **atomická** a funguje oběma směry. Při pokládání se posbírá i z několika zdrojových slotů (zadané množství bývá větší než velikost stacku) a při naplnění hromádky přeteče do vyšší v sloupci. Při sbírání se naopak bere přes několik pater odshora a v cílovém inventáři se rozloží do tolika slotů, kolik je potřeba.

## `output` — akce nastavení výstupu

Blok s `output X` nastaví **výstupní pin** zařízení na hodnotu X a nic nepřenáší. Přípustné hodnoty jsou **0 až 15**.

**Output bloky se ptají vždy na cíl** — tedy na ten konec, na kterém zařízení samo sedí. Prefix `in target` v nich psát nemusíš (a `in source` v nich nedává smysl; papír to nahlásí). Důvod je prostý: output kolej běží i tehdy, když se zrovna nic nepřenáší, takže není žádný předmět ze zdroje, kterého by se šlo zeptat.

```text
in target
*leather* 5+
*water* 30
output 5
```

Blok nastaví výstup na 5, jakmile je v cíli aspoň 5 kůží a 30 vody.

Output bloky **nebrání přenosu**. Jeden průchod obslouží obě koleje, takže si zařízení může současně něco přenést a hlásit stav. Na tom, kam output blok napíšeš, přesto záleží — akce mění stav cíle, a output blok nad ní a pod ní odpoví v témž tiku jinak.

### Nulování je automatické

Když v daném průchodu **neplatí žádný** output blok, pin je **0**. Nemusíš tedy psát resetovací `output 0` nad plnicí bloky, jak to vyžadovala starší verze — pin je obraz stavu, ne paměť poslední změny.

```text
# hlásí 15, dokud je v cíli plný sloup polen; jinak sám spadne na 0
in target
game:firewood 96
output 15
```

Staré papíry s resetovacím `output 0` nahoře fungují dál, jen v nich ten blok už není potřeba.

### `output .` (jen senzory)

Tečka místo čísla znamená „ohlas hodnotu, kterou blok spočítal" — u senzoru typicky číslo shodného slotu nebo úroveň zaplnění. U přenosových zařízení se zatím nepoužívá.

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

### Signalizuj na výstup po naplnění

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

Po naplnění (30 vody a aspoň 5 kůže v cíli) nastaví druhý blok výstupní pin na 5 — třeba pro spuštění další hadice přes Signals. Dokud podmínky neplatí, pin sám drží 0.

Přesně na tomhle stojí automatizovaný milíř: každý sloup má svou plnicí klapku, output jedné jde na input další, a ohniště se zapálí teprve když prostřední sloup hlásí 15, tedy 96 polen.

## Chyby v papíru

Papír s chybou se **přijme**, nikdy neodmítne — jedna překlepnutá řádka nesmí zastavit celý stroj, když zbytek papíru ještě píšeš. Vadné řádky se chovají jako neplatné a hlásí se dvěma kanály:

1. **Hned při přiložení papíru** na blok, jako ingame hláška.
2. **Trvale v tooltipu** bloku, červeně a úplně nahoře nad textem podmínek.

Hláška vždy uvádí **číslo řádku**, důvod a ten řádek:

```text
Chyby v papíru: řádek 6: target bere číslo slotu 1-14 (volitelně s ifEmpty),
„ground“, „ground N“ nebo „firepit“ („target sideways“)
```

Hlásí se neznámý rozsah, špatný `output` / `source` / `target` / `amount` / `slot`, neznámá akce, nesrozumitelná podmínka a `in source` v output bloku.

Dvě hlášení stojí za zvláštní zmínku, protože obě odhalují papír, který **vypadá správně a přitom tiše nedělá nic**:

- **vzor kódu s mezerou** (`game:planks *`) — nesedí na žádný kód, a protože se v bloku ANDuje, zabije celý blok;
- **blok, který neříká, co má přenášet** — postavený jen z `in target` podmínek nemá podle čeho vybrat náklad. Hlásí se jen tam, kde to je chyba: ventil žádný výběr nepotřebuje (zdrojem je vzdálený konec hadice) a senzor nic nepřenáší, takže ty se nehlásí.

## Omezení a důležité poznámky

- Jeden průchod, **dvě koleje**: output a akční. Output kolej vezme první platný output blok; akční kolej první akční blok, jehož akce odvede práci.
- **Neplatí-li žádný output blok, pin je 0.** Pin je obraz stavu, ne paměť poslední změny.
- Blok, který má u žlabu nebo klapky provést **přenos**, musí obsahovat aspoň jednu podmínku v rozsahu `in source` — jinak není podle čeho vybrat slot. U ventilu to neplatí (zdrojem je vzdálený konec hadice) a u output bloků taky ne.
- **Output bloky se ptají vždy na cíl**, ať už prefix napíšeš nebo ne.
- `slot N` u množství se ptá na jeden slot a je to **hlídka, ne výběr** — sama takový řádek blok
  k přenosu nekvalifikuje.
- `target`, `amount`, `ifEmpty` jsou **direktivy** — tvarují defaultní přenos, nejsou to samy o sobě podmínky.
- `do seal` je **akce** — nahrazuje defaultní akci bloku. Provede ji první platný blok, ne všechny.
- `amount` je velikost jedné dávky; vstupní signál 1–7 naplní buffer v kusech/litrech, který se čerpá o skutečně přenesené množství (ne o počet dávek). Signál `15` = průběžně.
- `do seal` má smysl jen tam, kde je v cílové pozici sud. `output` podporují všechna zařízení — žlab, klapka, ventil i senzor.
- Množství kapalin se vyhodnocuje v litrech, zatímco `stackSize` u kapaliny je interní počet porcí.
- U podmínek množství používej tečku jako desetinný oddělovač, například `2.75`.
