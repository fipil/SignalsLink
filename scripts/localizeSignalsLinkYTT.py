"""Local translation run for SignalsLink.YTT.

The bridge has no handbook, so there is no HTML step: cs.json is written by hand and is the
source of truth. Everything else is the SignalsLink script - same model, same prompt, same
fingerprint bookkeeping - only pointed at the bridge's lang folder and its own state file.

    Step 2 (AI)      cs.json -> de/en/es/fr/it/pl/pt/ru/sk.json

Commit scripts/localize-state-ytt.json along with the translations.

The API key is read from the OPENAI_API_KEY environment variable.

    python scripts/localizeSignalsLinkYTT.py                 # the usual run
    python scripts/localizeSignalsLinkYTT.py --check         # no AI, just say what is stale
    python scripts/localizeSignalsLinkYTT.py --langs de,pl   # just these languages
    python scripts/localizeSignalsLinkYTT.py --force         # retranslate everything

BOM: see localizeSignalsLink.py - every read strips one, no write adds one.
"""

import argparse
import os
import sys
from pathlib import Path

import localizeSignalsLink as core

MOD = "SignalsLink.YTT"
LANG_DIR = Path(__file__).resolve().parent.parent / MOD / "assets" / "signalslinkytt" / "lang"

# The shared functions read their paths from the SignalsLink module, so they are pointed at the
# bridge here, once, before anything is called.
core.MOD = MOD
core.LANG_DIR = LANG_DIR
core.CS_PATH = LANG_DIR / "cs.json"
core.STATE_PATH = Path(__file__).resolve().parent / "localize-state-ytt.json"


def parse_args():
    parser = argparse.ArgumentParser(description="Prelozi SignalsLink.YTT z cestiny do ostatnich jazyku.")
    parser.add_argument("--langs", help="carkou oddeleny seznam jazyku (vychozi: %s)" % ",".join(core.DEFAULT_LANGS))
    parser.add_argument("--model", default=core.DEFAULT_MODEL, help="model (vychozi: %s)" % core.DEFAULT_MODEL)
    parser.add_argument("--chunk", type=int, default=core.DEFAULT_CHUNK, help="klicu na jeden pozadavek")
    parser.add_argument("--force", action="store_true", help="prelozit vsechno znovu, i hotove")
    parser.add_argument("--check", action="store_true", help="bez AI: jen vypsat, co by se prekladalo")
    return parser.parse_args()


def main():
    args = parse_args()

    if not core.CS_PATH.exists():
        raise SystemExit("Chybi %s" % core.CS_PATH)

    cs_json = core.read_json(core.CS_PATH)
    core.validate_one_line(cs_json, "cs.json")
    print("cs.json ma %d klicu." % len(cs_json))

    langs = [l.strip() for l in args.langs.split(",")] if args.langs else list(core.DEFAULT_LANGS)
    langs = [l for l in langs if l]

    state = core.load_state(cs_json, core.DEFAULT_LANGS)

    if args.check:
        print("\nBez prekladu (--check). Chybelo by prelozit:")
        for lang in langs:
            lang_json = core.read_json(LANG_DIR / (lang + ".json"))
            print("   %-3s %d klicu" % (lang, len(core.stale_keys(lang, lang_json, cs_json, state))))
        return 0

    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        raise SystemExit(
            "Chybi promenna prostredi OPENAI_API_KEY.\n"
            "Nastavit ji muzes treba takto (PowerShell, trvale pro uzivatele):\n"
            '   setx OPENAI_API_KEY "sk-..."\n'
            "a pak otevrit nove okno terminalu."
        )

    if not core.PROMPT_PATH.exists():
        raise SystemExit("Chybi prompt %s" % core.PROMPT_PATH)

    system_prompt = core.read_text(core.PROMPT_PATH)

    print("\nModel %s, %d jazyku%s" % (args.model, len(langs), ", FORCE" if args.force else ""))

    total = 0
    for lang in langs:
        total += core.translate_language(api_key, args.model, args.chunk, system_prompt,
                                         lang, cs_json, state, args.force)

    core.save_state(state)

    print("\nHotovo, prelozeno %d klicu celkem. Zkontroluj `git diff` a pak commitni" % total)
    print("vcetne scripts/localize-state-ytt.json.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
