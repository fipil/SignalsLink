"""Local translation run for SignalsLink.YTT.

The same two steps as SignalsLink, and the same code: this only points the SignalsLink script
at the bridge's lang folder and gives it a state file of its own.

    Step 1 (no AI)   signalslinkytt.html -> cs.json
    Step 2 (AI)      cs.json -> de/en/es/fr/it/pl/pt/ru/sk.json

The guide text lives in signalslinkytt.html, one <translation key="..."> each; the short strings
are written into cs.json by hand. Czech is the source of truth.

Commit scripts/localize-state-ytt.json along with the translations.

The API key is read from the OPENAI_API_KEY environment variable.

    python scripts/localizeSignalsLinkYTT.py                 # the usual run
    python scripts/localizeSignalsLinkYTT.py --seed-only     # only step 1, then say what is stale
    python scripts/localizeSignalsLinkYTT.py --langs de,pl   # just these languages
    python scripts/localizeSignalsLinkYTT.py --force         # retranslate everything

BOM: see localizeSignalsLink.py - every read strips one, no write adds one.
"""

import sys
from pathlib import Path

import localizeSignalsLink as core

MOD = "SignalsLink.YTT"
LANG_DIR = Path(__file__).resolve().parent.parent / MOD / "assets" / "signalslinkytt" / "lang"

# The shared functions read their paths from the SignalsLink module, so they are pointed at the
# bridge here, once, before anything is called.
core.MOD = MOD
core.LANG_DIR = LANG_DIR
core.HTML_PATH = LANG_DIR / "signalslinkytt.html"
core.CS_PATH = LANG_DIR / "cs.json"
core.STATE_PATH = Path(__file__).resolve().parent / "localize-state-ytt.json"


if __name__ == "__main__":
    sys.exit(core.main())
