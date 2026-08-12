# Podmínky na papíře pro ManagedChute

Tento dokument popisuje syntaxi podmínek na papíře používanou komponentou **ManagedChute**. Napište text na papír a papír vložte do spravovaného žlabu.

Podmínky na papíře umožňují žlabu:

- vybírat zdrojové předměty, které se smějí přesunout;
- kontrolovat zdrojový nebo cílový inventář;
- směrovat odpovídající předměty do konkrétního cílového slotu;
- přesunout přesný počet předmětů nebo objem kapaliny;
- vyžadovat původně prázdný cílový slot;
- zapečetit cílový sud, jsou-li splněny jeho cílové podmínky.

## Základy

Blok podmínek tvoří jeden odstavec. Jednotlivé bloky oddělte jedním nebo více prázdnými řádky.

```text
podmínka A
podmínka B
instrukce

podmínka C
instrukce
```

V rámci jednoho bloku musí být splněny **všechny podmínky**. Bloky se vyhodnocují shora dolů; přesun řídí první blok odpovídající vybranému zdrojovému předmětu.

Komentáře a prázdné řádky uvnitř bloku se ignorují:

```text
# Přesouvej pouze silný tanin
*strongtannin*

// Toto je také komentář
```

Pro komentář použijte na začátku řádku `#` nebo `//`.

## Co se vyhodnocuje ve výchozím stavu

Pokud rozsah nezmění direktiva, podmínky se vztahují na **kandidátní předmět ve zdrojovém inventáři**. Žlab prochází zdrojový inventář podle signálu zdrojového slotu a vybere první předmět odpovídající některému bloku.

Například:

```text
game:resin
```

Pro přesun bude vybrána pouze pryskyřice.

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

Direktivy přesunu jsou metadata odpovídajícího bloku. Nejsou to samy o sobě podmínky.

### `target <slot>`

Nahradí signál cílového slotu žlabu pro odpovídající blok. Sloty jsou číslované **od jedné**.

```text
game:resin
target 4
```

Tím se odpovídající pryskyřice odešle do slotu 4 cílového inventáře.

### `target <slot> ifEmpty`

Vyžaduje, aby byl určený cílový slot prázdný, než tento blok smí zahájit přesun.

```text
game:resin
target 4 ifEmpty
amount 3
```

To znamená:

1. cílový slot 4 musí být při zahájení dávky prázdný;
2. žlab do něj přesune přesně tři odpovídající kusy pryskyřice;
3. po zahájení dávky už slot může obsahovat právě přesouvané předměty; `ifEmpty` nezastaví zbytek téže dávky.

U varných slotů ohniště není neaktivní varný slot považován za použitelný; musí v něm být hrnec nebo jiná varná nádoba.

### `amount <množství>`

Nahradí běžné přenášené množství pro odpovídající blok.

```text
game:resin
amount 3
```

Pokud blok obsahuje `amount`, vstupní signál žlabu slouží jako **spouštěč**, nikoli jako počet kusů nebo litrů k přesunu. Jedna úspěšná odpovídající dávka přenese zadané množství.

Množství může být desetinné s tečkou jako oddělovačem:

```text
game:water-*
amount 2.75
```

U kapalin představuje hodnota litry a podporuje maximálně dvě desetinná místa. U běžných předmětů se desetinná část zahodí; pokud je zadané množství větší než nula, výsledkem je alespoň jeden kus.

Při přesunu běžných předmětů může žlab spojit odpovídající stejné stacky z více zdrojových slotů. Je-li pro direktivu `amount` k dispozici méně kusů, celá dávka se nespustí.

## Výstupní direktivy

### `output <hodnota>`

Nastaví výstupní signál pro odpovídající blok. Přípustné jsou hodnoty od 1 do 14.

```text
game:resin
output 5
```

Tento blok při shodě nastaví výstup žlabu na hodnotu 5.

### `output .`

Tečka nastaví výchozí výstupní chování. To odpovídá stavu, kdy blok nenastaví konkrétní hodnotu výstupu.

```text
game:resin
output .
```

## Akce

### `do seal`

`do seal` zapečetí cílový sud, pokud celý blok odpovídá. Akce je určena pro sud v cílové pozici žlabu.

```text
in target
game:water-* 50+
do seal
```

Příklad zapečetí cílový sud, pokud obsahuje alespoň 50 litrů vody. Akce se kontrolují při pokusu o přesun i bezprostředně po úspěšném přesunu, takže sud lze zapečetit i tehdy, když už cílové podmínky byly splněné před dalším přesunem.

Zapečetěný sud nelze touto akcí zapečetit znovu.

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
in source
game:water-*
in target
game:water-* 50+
do seal
```

## Omezení a důležité poznámky

- Každý blok, který má vybírat předmět pro přesun, musí obsahovat alespoň jednu podmínku v rozsahu `in source`.
- `target`, `amount` a `output` jsou direktivy, nikoli podmínky; samy o sobě nemohou vybrat zdrojový předmět.
- `do seal` je akce nad cílem. Má smysl pouze tehdy, když cíl žlabu je sud.
- Množství kapalin se vyhodnocuje v litrech, zatímco `stackSize` pro kapalinu představuje interní počet porcí.
- U podmínek množství používejte tečku jako desetinný oddělovač, například `2.75`.
- První blok odpovídající zdrojovému předmětu určuje direktivy jeho přesunu. Bloky s akcemi se navíc kontrolují vůči jejich příslušnému zdrojovému a cílovému rozsahu.
