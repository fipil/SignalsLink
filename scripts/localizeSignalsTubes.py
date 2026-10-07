"""Local translation run for SignalsTubes.

The same two steps as SignalsLink, and the same code: this only points the SignalsLink script
at the tubes mod's lang folder and gives it a state file of its own.

    Step 1 (no AI)   signalstubes.html -> cs.json
    Step 2 (AI)      cs.json -> de/en/es/fr/it/pl/pt/ru/sk.json

The long handbook texts live in signalstubes.html, one <translation key="..."> each; the
short strings are written into cs.json by hand. Czech is the source of truth.

Commit scripts/localize-state-tubes.json along with the translations.

The API key is read from the OPENAI_API_KEY environment variable.

    python scripts/localizeSignalsTubes.py                 # the usual run
    python scripts/localizeSignalsTubes.py --seed-only     # only step 1, then report
    python scripts/localizeSignalsTubes.py --langs de,pl   # just these languages
    python scripts/localizeSignalsTubes.py --force         # retranslate everything
    python scripts/localizeSignalsTubes.py --accept en     # English written by hand: record it as current

BOM: see localizeSignalsLink.py - every read strips one, no write adds one.
"""

import sys
from pathlib import Path

import localizeSignalsLink as core

MOD = "SignalsTubes"
LANG_DIR = Path(__file__).resolve().parent.parent / MOD / "assets" / "signalstubes" / "lang"

# The shared functions read their paths from the SignalsLink module, so they are pointed at the
# tubes mod here, once, before anything is called.
core.MOD = MOD
core.LANG_DIR = LANG_DIR
core.HTML_PATH = LANG_DIR / "signalstubes.html"
core.CS_PATH = LANG_DIR / "cs.json"
core.STATE_PATH = Path(__file__).resolve().parent / "localize-state-tubes.json"


if __name__ == "__main__":
    sys.exit(core.main())
