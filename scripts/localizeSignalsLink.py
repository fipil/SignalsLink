"""Local translation run for SignalsLink.

Does the same two steps as the GitHub action in .github/scripts/localize-signalslink.mjs,
but from your own machine, so you can look at the result before committing it.

    Step 1 (no AI)   signalslink.html -> cs.json
    Step 2 (AI)      cs.json -> de/en/es/fr/it/pl/pt/ru/sk.json

Only the keys that actually need it are translated. What "need" means is recorded in
scripts/localize-state.json: for every language and every key it holds a fingerprint of the
Czech text that translation was made from. A key is stale when the fingerprint no longer
matches, or when the language file has not got it at all.

That file is the point. Anything derived from "what changed since the last run" or "what
changed since the last commit" is lost the moment you run the script twice, commit in the
wrong order, or have a run stop half way through the languages - and then a stale translation
silently stays stale. A fingerprint does not care about any of that.

Commit scripts/localize-state.json along with the translations.

The API key is read from the OPENAI_API_KEY environment variable.

    python scripts/localizeSignalsLink.py                 # the usual run
    python scripts/localizeSignalsLink.py --seed-only     # only step 1, then report
    python scripts/localizeSignalsLink.py --langs de,pl   # just these languages
    python scripts/localizeSignalsLink.py --force         # retranslate everything

BOM: the lang JSON files must be UTF-8 **without** a BOM, and no BOM may survive inside a
string value either - the game misbehaves otherwise. Every read strips one, no write adds
one, and values coming back from the model are stripped as well. Do not "simplify" this.
"""

import argparse
import hashlib
import io
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

MOD = "SignalsLink"
LANG_DIR = Path(__file__).resolve().parent.parent / MOD / "assets" / "signalslink" / "lang"
HTML_PATH = LANG_DIR / "signalslink.html"
CS_PATH = LANG_DIR / "cs.json"
REPO_ROOT = Path(__file__).resolve().parent.parent
PROMPT_PATH = REPO_ROOT / ".github" / "prompts" / "localize-signalslink.md"

# Deliberately NOT in the lang folder: the game loads every json it finds there.
STATE_PATH = Path(__file__).resolve().parent / "localize-state.json"

DEFAULT_LANGS = ["de", "en", "es", "fr", "it", "pl", "pt", "ru", "sk"]
DEFAULT_MODEL = os.environ.get("OPENAI_MODEL", "gpt-5.6-terra")
DEFAULT_CHUNK = 30

API_URL = "https://api.openai.com/v1/chat/completions"
REQUEST_TIMEOUT_S = 600
MAX_ATTEMPTS = 4

# Keys no longer in cs.json are dropped from the language files, so a removed string does not
# linger in nine translations forever.
PRUNE_EXTRA_KEYS = True


# --------------------------------------------------------------------------- BOM-safe I/O

def strip_bom(text):
    return text[1:] if text.startswith("﻿") else text


def strip_bom_deep(value):
    """A BOM inside a *value* is the one that breaks the game, and it is easy to import one
    from a model response or a hand-edited file."""
    if isinstance(value, str):
        return strip_bom(value)
    if isinstance(value, list):
        return [strip_bom_deep(item) for item in value]
    if isinstance(value, dict):
        return {key: strip_bom_deep(item) for key, item in value.items()}
    return value


def read_text(path):
    with io.open(path, encoding="utf-8-sig", newline="") as handle:
        return strip_bom(handle.read())


def read_json(path):
    if not path.exists():
        return {}
    return strip_bom_deep(json.loads(read_text(path)))


def write_json(path, data, sort_keys=False):
    """UTF-8, no BOM, LF endings, two-space indent, real accented characters, trailing
    newline - byte for byte what the GitHub action writes, so the two never fight."""
    text = json.dumps(strip_bom_deep(data), ensure_ascii=False, indent=2, sort_keys=sort_keys) + "\n"
    with io.open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


# --------------------------------------------------------------------------- step 1: seed cs

TRANSLATION_RE = re.compile(r'<translation\s+key="([^"]+)"[^>]*>(.*?)</translation>', re.S)


def to_one_line(html):
    """Flatten a handbook entry to a single line, exactly the way the action does it: strip
    the indentation, drop blank lines, and end every line with one space."""
    lines = re.split(r"\r?\n", html)
    lines = [line.lstrip() for line in lines]
    lines = [line for line in lines if line]
    return "".join(line.rstrip() + " " for line in lines)


def seed_cs_from_html():
    """Writes the handbook entries into cs.json and returns it."""
    html = read_text(HTML_PATH)
    cs_before = read_json(CS_PATH)
    cs_after = dict(cs_before)

    entries = TRANSLATION_RE.findall(html)
    for key, inner in entries:
        cs_after[key] = to_one_line(inner)

    if cs_after != cs_before:
        write_json(CS_PATH, cs_after)

    print("Krok 1: v signalslink.html je %d zaznamu, cs.json ma %d klicu." % (len(entries), len(cs_after)))
    return cs_after


def fingerprint(text):
    return hashlib.sha1(text.encode("utf-8")).hexdigest()[:12]


def read_cs_from_head():
    """cs.json as of the last commit, or None. Used once, to bootstrap the state file."""
    rel = CS_PATH.relative_to(REPO_ROOT).as_posix()

    try:
        done = subprocess.run(["git", "show", "HEAD:" + rel], cwd=str(REPO_ROOT), capture_output=True)
    except OSError:
        return None

    if done.returncode != 0:
        return None

    try:
        return strip_bom_deep(json.loads(done.stdout.decode("utf-8-sig")))
    except (ValueError, UnicodeDecodeError):
        return None


def load_state(cs_json, langs):
    """Which Czech text each translation was made from.

    The first time there is no such file, so it is bootstrapped from the last commit: every
    language is taken to be current with the Czech that was committed, for the keys it actually
    has. That is true right after a normal translate-then-commit, and any key it gets wrong is
    simply translated once more.
    """
    if STATE_PATH.exists():
        return read_json(STATE_PATH).get("languages", {})

    baseline = read_cs_from_head()

    if baseline is None:
        print("Poznamka: stav prekladu jeste neexistuje a cs.json z posledniho commitu se")
        print("          nepodarilo precist. Doplni se jen klice, ktere jazykum chybi;")
        print("          pro uplny preklad pouzij --force.")
        return {lang: {k: fingerprint(v) for k, v in cs_json.items()} for lang in langs}

    print("Zakladam stav prekladu podle posledniho commitu (scripts/localize-state.json).")

    state = {}
    for lang in langs:
        lang_json = read_json(LANG_DIR / (lang + ".json"))
        state[lang] = {k: fingerprint(v) for k, v in baseline.items() if k in lang_json}

    # Written out at once. From here on the last commit is irrelevant, so committing in the
    # "wrong" order can no longer lose track of anything.
    save_state(state)
    return state


def save_state(state):
    write_json(STATE_PATH, {"version": 1, "languages": state}, sort_keys=True)


def stale_keys(lang, lang_json, cs_json, state):
    """Keys this language is missing or whose Czech has moved on since it was translated."""
    known = state.get(lang, {})

    return sorted(key for key, text in cs_json.items()
                  if key not in lang_json or known.get(key) != fingerprint(text))


# --------------------------------------------------------------------------- validation

def validate_one_line(items, label):
    for key, value in items.items():
        if not isinstance(value, str):
            raise ValueError("%s: klic '%s' neni text" % (label, key))
        if "\r" in value or "\n" in value:
            raise ValueError("%s: klic '%s' obsahuje konec radku" % (label, key))


def validate_result(result, items, label):
    if not isinstance(result, dict):
        raise ValueError("%s: model nevratil objekt klic->hodnota" % label)

    expected = sorted(items)
    got = sorted(result)
    if expected != got:
        missing = [k for k in expected if k not in result]
        extra = [k for k in got if k not in items]
        raise ValueError("%s: model vratil jine klice (chybi %s, navic %s)" % (label, missing, extra))

    validate_one_line(result, label)


# --------------------------------------------------------------------------- step 2: translate

def call_openai(api_key, model, system_prompt, target_lang, items):
    payload = {
        "model": model,
        "response_format": {"type": "json_object"},
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": json.dumps({"targetLanguage": target_lang, "items": items},
                                                   ensure_ascii=False)},
        ],
    }

    request = urllib.request.Request(
        API_URL,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json", "Authorization": "Bearer " + api_key},
        method="POST",
    )

    with urllib.request.urlopen(request, timeout=REQUEST_TIMEOUT_S) as response:
        body = json.loads(response.read().decode("utf-8"))

    content = strip_bom(body["choices"][0]["message"]["content"] or "")

    try:
        return strip_bom_deep(json.loads(content))
    except json.JSONDecodeError:
        raise ValueError("model vratil nevalidni JSON: " + content[:400])


def call_openai_with_retry(api_key, model, system_prompt, target_lang, items, label):
    for attempt in range(1, MAX_ATTEMPTS + 1):
        try:
            result = call_openai(api_key, model, system_prompt, target_lang, items)
            validate_result(result, items, label)
            return result
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")[:400]
            # A refused request will be refused again; only server-side trouble is worth retrying.
            if error.code < 500 or attempt == MAX_ATTEMPTS:
                raise RuntimeError("OpenAI API %s: %s" % (error.code, detail))
            print("   HTTP %s, pokus %d/%d" % (error.code, attempt, MAX_ATTEMPTS))
        except (urllib.error.URLError, ValueError, KeyError) as error:
            if attempt == MAX_ATTEMPTS:
                raise
            print("   %s (pokus %d/%d)" % (error, attempt, MAX_ATTEMPTS))

        time.sleep(min(30, 2 ** (attempt - 1)))


def chunked(items, size):
    for start in range(0, len(items), size):
        yield items[start:start + size]


def translate_language(api_key, model, chunk_size, system_prompt, lang, cs_json, state, force):
    lang_path = LANG_DIR / (lang + ".json")
    lang_json = read_json(lang_path)
    known = state.setdefault(lang, {})

    removed = 0
    if PRUNE_EXTRA_KEYS:
        for key in [k for k in lang_json if k not in cs_json]:
            del lang_json[key]
            known.pop(key, None)
            removed += 1

    missing = [k for k in cs_json if k not in lang_json]
    todo = sorted(cs_json) if force else stale_keys(lang, lang_json, cs_json, state)

    if not todo:
        if removed:
            write_json(lang_path, lang_json)
            save_state(state)
            print("[%s] nic k prekladu, odebrano %d zrusenych klicu" % (lang, removed))
        else:
            print("[%s] nic k prekladu" % lang)
        return 0

    print("[%s] k prekladu %d klicu (%d chybejicich, %d zastaralych)"
          % (lang, len(todo), len(missing), len(todo) - len(missing)))

    batches = list(chunked(todo, chunk_size))
    for index, batch in enumerate(batches, start=1):
        items = {key: cs_json[key] for key in batch}
        validate_one_line(items, "Vstupni CS pro %s" % lang)

        print("   davka %d/%d (%d klicu)" % (index, len(batches), len(batch)))
        result = call_openai_with_retry(api_key, model, system_prompt, lang, items,
                                        "Vystup %s" % lang)

        lang_json.update(result)
        for key in batch:
            known[key] = fingerprint(cs_json[key])

        # Saved per batch, so a run that dies in the middle keeps what it already paid for.
        write_json(lang_path, lang_json)
        save_state(state)

    print("[%s] ulozeno: %s" % (lang, lang_path.name))
    return len(todo)


# --------------------------------------------------------------------------- main

def parse_args():
    parser = argparse.ArgumentParser(description="Prelozi SignalsLink z cestiny do ostatnich jazyku.")
    parser.add_argument("--langs", help="carkou oddeleny seznam jazyku (vychozi: %s)" % ",".join(DEFAULT_LANGS))
    parser.add_argument("--model", default=DEFAULT_MODEL, help="model (vychozi: %s)" % DEFAULT_MODEL)
    parser.add_argument("--chunk", type=int, default=DEFAULT_CHUNK, help="klicu na jeden pozadavek")
    parser.add_argument("--force", action="store_true", help="prelozit vsechno znovu, i hotove")
    parser.add_argument("--seed-only", action="store_true",
                        help="jen propsat html do cs.json a vypsat, co by se prekladalo")
    return parser.parse_args()


def main():
    args = parse_args()

    if not HTML_PATH.exists():
        raise SystemExit("Chybi %s" % HTML_PATH)

    cs_json = seed_cs_from_html()

    langs = [l.strip() for l in args.langs.split(",")] if args.langs else list(DEFAULT_LANGS)
    langs = [l for l in langs if l]

    state = load_state(cs_json, DEFAULT_LANGS)

    if args.seed_only:
        print("\nKrok 2 preskocen (--seed-only). Chybelo by prelozit:")
        for lang in langs:
            lang_json = read_json(LANG_DIR / (lang + ".json"))
            print("   %-3s %d klicu" % (lang, len(stale_keys(lang, lang_json, cs_json, state))))
        return 0

    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        raise SystemExit(
            "Chybi promenna prostredi OPENAI_API_KEY.\n"
            "Nastavit ji muzes treba takto (PowerShell, trvale pro uzivatele):\n"
            '   setx OPENAI_API_KEY "sk-..."\n'
            "a pak otevrit nove okno terminalu."
        )

    if not PROMPT_PATH.exists():
        raise SystemExit("Chybi prompt %s" % PROMPT_PATH)

    system_prompt = read_text(PROMPT_PATH)

    print("\nKrok 2: model %s, %d jazyku%s" % (args.model, len(langs), ", FORCE" if args.force else ""))

    total = 0
    for lang in langs:
        total += translate_language(api_key, args.model, args.chunk, system_prompt,
                                    lang, cs_json, state, args.force)

    save_state(state)

    print("\nHotovo, prelozeno %d klicu celkem. Zkontroluj `git diff` a pak commitni" % total)
    print("vcetne scripts/localize-state.json.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
