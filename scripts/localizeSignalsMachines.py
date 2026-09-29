"""Local translation run for SignalsMachines.

The same two steps as SignalsLink, and the same code: this only points the SignalsLink script
at the machines mod's lang folder and gives it a state file of its own.

    Step 1 (no AI)   signalsmachines.html -> cs.json
    Step 2 (AI)      cs.json -> de/en/es/fr/it/pl/pt/ru/sk.json

The long handbook texts live in signalsmachines.html, one <translation key="..."> each; the
short strings are written into cs.json by hand. Czech is the source of truth.

Commit scripts/localize-state-machines.json along with the translations.

The API key is read from the OPENAI_API_KEY environment variable.

    python scripts/localizeSignalsMachines.py                 # the usual run
    python scripts/localizeSignalsMachines.py --seed-only     # only step 1, then report
    python scripts/localizeSignalsMachines.py --langs de,pl   # just these languages
    python scripts/localizeSignalsMachines.py --force         # retranslate everything

BOM: see localizeSignalsLink.py - every read strips one, no write adds one.
"""

import sys
from pathlib import Path

import localizeSignalsLink as core

MOD = "SignalsMachines"
LANG_DIR = Path(__file__).resolve().parent.parent / MOD / "assets" / "signalsmachines" / "lang"

# The shared functions read their paths from the SignalsLink module, so they are pointed at the
# machines mod here, once, before anything is called.
core.MOD = MOD
core.LANG_DIR = LANG_DIR
core.HTML_PATH = LANG_DIR / "signalsmachines.html"
core.CS_PATH = LANG_DIR / "cs.json"
core.STATE_PATH = Path(__file__).resolve().parent / "localize-state-machines.json"


if __name__ == "__main__":
    sys.exit(core.main())
