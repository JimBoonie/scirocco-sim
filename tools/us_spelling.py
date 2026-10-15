#!/usr/bin/python3
# @description - US-English spelling checker and fixer for this project. "check" lists British spellings left in the given directories (exit 1 if any); "fix" rewrites them in place with the American spelling (identifiers, comments, UI strings, docs). Protected text is never changed: URLs, external file and object names (see PROTECTED_PATTERNS), double-quoted text in prose markdown (sourced quotes and titles), and, in the historical files CHANGELOG.md and docs/RENAMES.md, every `code span` (it holds old identifier names on purpose).
USAGE = "Usage: us_spelling.py check|fix <directory-or-file> [...]   |   us_spelling.py yaml-keys <unity-yaml-file> [...]"
import os
import re
import sys

YAML_KEY_PATTERN = re.compile(r"^(\s*(?:- )?)([A-Za-z_][A-Za-z0-9_]*)(:)", re.MULTILINE)   # a serialized field name at the start of a Unity YAML line

# (British stem, American stem, needs_word_start). A stem with needs_word_start only matches at the start of a word or after a capital (so "styrene" is not "stire-ne").
STEM_RULES = [
    ("centre(?-i:d)", "centered", False), ("centring", "centering", False), ("centre", "center", False),
    ("tyre", "tire", True), ("colour", "color", False), ("behaviour", "behavior", False), ("neighbour", "neighbor", False),
    ("favour", "favor", False), ("honour", "honor", False), ("aluminium", "aluminum", False), ("artefact", "artifact", False),
    ("catalogue", "catalog", False), ("analogue", "analog", False), ("manoeuvre", "maneuver", False),
    ("cancelled", "canceled", False), ("labelled", "labeled", False), ("modelled", "modeled", False), ("modelling", "modeling", False),
    ("travelled", "traveled", False), ("travelling", "traveling", False), ("tunnelling", "tunneling", False), ("fuelled", "fueled", False), ("fuelling", "fueling", False),
    ("kilometre", "kilometer", False), ("millimetre", "millimeter", False), ("centimetre", "centimeter", False), ("metre", "meter", True),
    ("kerb", "curb", True), ("grey", "gray", True), ("lagunakerb", "lagunacurb", False),
]
# -ise / -isation words: stem + suffix, never matching words such as "realistic" or "criticism".
IZE_STEMS = ["normalis", "regularis", "initialis", "linearis", "quantis", "optimis", "maximis", "minimis", "summaris", "criticis", "digitis", "generalis", "realis", "utilis", "synthesis", "stabilis", "characteris", "visualis", "organis", "recognis", "parameteris", "standardis", "specialis", "customis", "finalis", "penalis", "prioritis", "randomis", "serialis", "synchronis", "categoris", "emphasis", "capitalis", "modernis"]
IZE_SUFFIXES = ["ation", "ations", "ed", "es", "er", "ers", "ing", "e"]
# analyse / analysed / analysing (the noun "analyses" is left alone)
ANALYSE_PATTERN = re.compile(r"analys((?-i:ed|ing|e))(?-i:(?![a-z]))", re.IGNORECASE)
PROTECTED_PATTERNS = [
    r"https?://[^\s)>\"'`]+",                      # URLs (host names such as heritagepartscentre.com)
    r"\b[A-Za-z]+_Kerbs?[A-Za-z_]*",              # Blender object, material and texture names: COL_Kerbs, M_Kerb, T_Kerb_RedWhite
    r"MonoBehaviour", r"\bBehaviour\b(?= [a-z]\w*;)", r"\bGreys\b",   # Unity and matplotlib API names
    r"Track_Kerbs", r"centreline\.csv", r"viscoelastic_tyre_research\.md", r"analyse\.py", r"heritagepartscentre",
]
SKIP_DIRECTORIES = {".git", "Library", "Logs", "UserSettings", "__pycache__", "obj", "Temp", "node_modules", "transcripts"}   # transcripts hold verbatim conversation text
# Old build snapshots (directories under builds/ and *.pre_* files) are historical records and are not rewritten.
TEXT_EXTENSIONS = (".cs", ".py", ".sh", ".md", ".cpp", ".txt", ".json")
# Files that are never rewritten: Unity-managed JSON and this script.
SKIP_FILE_NAMES = {"manifest.json", "packages-lock.json", "us_spelling.py"}   # the rule file itself lists the British spellings
HISTORICAL_FILES = ("CHANGELOG.md", "RENAMES.md")


# @description - Builds the compiled (pattern, replacement) list for every rule.
# @return {list} - tuples of (compiled regex, American replacement lowercase)
def build_rules():
    rules = []
    for british, american, needs_word_start in STEM_RULES:
        start = r"(?<![A-Za-z])" if needs_word_start else ""
        rules.append((re.compile(start + british, re.IGNORECASE), american))
        if needs_word_start:   # camelCase: a capitalized stem inside an identifier (frontTyre)
            rules.append((re.compile(british.capitalize()), american.capitalize()))
    for stem in IZE_STEMS:
        american_stem = stem[:-1] + "z"
        rules.append((re.compile(stem + "((?-i:" + "|".join(IZE_SUFFIXES) + r"))(?-i:(?![a-z]))", re.IGNORECASE), american_stem + r"\1"))
    return rules


RULES = build_rules()


# @description - Matches the capitalization of a matched word onto its replacement.
# @param {str} matched - the text that matched (for example "Centre" or "CENTRE")
# @param {str} replacement - lowercase American replacement
# @return {str} - replacement with the same capitalization pattern
def match_case(matched, replacement):
    if matched.isupper() and len(matched) > 1:
        return replacement.upper()
    if matched[0].isupper():
        return replacement[0].upper() + replacement[1:]
    return replacement


# @description - Expands the \\1 suffix reference of an -ise rule (the matched ending, for example "ation" in "normalisation").
# @param {re.Match} match - the rule's match
# @param {str} replacement - American stem, optionally ending in \\1
# @return {str} - replacement with the matched ending substituted
def expand_group(match, replacement):
    return replacement.replace("\\1", match.group(1)) if "\\1" in replacement else replacement


# @description - Replaces protected spans (URLs, external names, quotes, code spans) with placeholders so the rules cannot touch them.
# @param {str} text - file text
# @param {str} path - file path (decides which protections apply)
# @return {tuple} - (text with placeholders, list of the protected strings in order)
def protect(text, path):
    protected = []
    patterns = list(PROTECTED_PATTERNS)
    base = os.path.basename(path)
    if base in HISTORICAL_FILES:
        patterns.append(r"`[^`\n]*`")
    elif path.endswith(".md"):
        patterns.append(r"\"[^\"\n]*\"")
        patterns.append(r"“[^”\n]*”")
    combined = re.compile("|".join("(?:" + pattern + ")" for pattern in patterns))

    # @description - Stores one protected match and returns its placeholder.
    # @param {re.Match} match - protected span
    # @return {str} - placeholder token
    def stash(match):
        protected.append(match.group(0))
        return "\x00%d\x00" % (len(protected) - 1)

    return combined.sub(stash, text), protected


# @description - Applies every spelling rule to text that has no protected spans.
# @param {str} text - text with placeholders
# @return {str} - American-spelled text
def apply_rules(text):
    for pattern, replacement in RULES:
        text = pattern.sub(lambda match, replacement=replacement: match_case(match.group(0), expand_group(match, replacement)), text)
    text = ANALYSE_PATTERN.sub(lambda match: match_case(match.group(0), "analyz" + match.group(1)), text)
    return text


# @description - Returns the American-spelled version of a whole file text, honoring the protections.
# @param {str} text - original file text
# @param {str} path - file path
# @return {str} - fixed text
def fix_text(text, path):
    stashed, protected = protect(text, path)
    fixed = apply_rules(stashed)
    return re.sub("\x00(\\d+)\x00", lambda match: protected[int(match.group(1))], fixed)


# @description - Yields every text file under a directory that the spelling rules cover.
# @param {str} root - directory to walk
# @return {iterator} - file paths
def text_files(root):
    if os.path.isfile(root):
        yield root
        return
    for directory, subdirectories, names in os.walk(root):
        subdirectories[:] = [name for name in subdirectories if name not in SKIP_DIRECTORIES]
        if os.path.basename(directory) == "builds":
            subdirectories[:] = []   # snapshots of earlier builds
        for name in names:
            if name.endswith(TEXT_EXTENSIONS) and name not in SKIP_FILE_NAMES and ".pre_" not in name:
                yield os.path.join(directory, name)


# @description - Finds the British spellings that remain in one file (lines where fixing would change the text).
# @param {str} path - file path
# @return {list} - (line number, line) pairs
def remaining_lines(path):
    with open(path, encoding="utf-8", errors="ignore") as handle:
        text = handle.read()
    fixed = fix_text(text, path)
    if fixed == text:
        return []
    return [(number + 1, original) for number, (original, changed) in enumerate(zip(text.split("\n"), fixed.split("\n"))) if original != changed]


# @description - Renames serialized field keys (never values) in one Unity YAML scene or asset file, so values survive the C# field renames. Unity class names such as MonoBehaviour are kept.
# @param {str} path - YAML file path
# @return {int} - number of keys renamed
def rename_yaml_keys(path):
    counter = [0]
    with open(path, encoding="utf-8", newline="") as handle:
        text = handle.read()

    # @description - Renames one key match.
    # @param {re.Match} match - indentation, key and colon
    # @return {str} - the line start with the American key
    def rename(match):
        key = match.group(2)
        new_key = key if key == "MonoBehaviour" else apply_rules(key)
        counter[0] += new_key != key
        return match.group(1) + new_key + match.group(3)

    renamed = YAML_KEY_PATTERN.sub(rename, text)
    if renamed != text:
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(renamed)
    return counter[0]


# @description - Entry point: "yaml-keys" renames Unity YAML keys; "check" prints remaining British spellings and returns 1 if any; "fix" rewrites files in place and prints the changed file list.
# @param {list} arguments - command-line arguments (mode, then directories)
# @return {int} - process exit status
def main(arguments):
    if len(arguments) < 2 or arguments[0] not in ("check", "fix", "yaml-keys"):
        print(USAGE)
        return 2
    if arguments[0] == "yaml-keys":
        for yaml_path in arguments[1:]:
            print("%s: %d keys renamed" % (yaml_path, rename_yaml_keys(yaml_path)))
        return 0
    mode, roots = arguments[0], arguments[1:]
    total = 0
    for root in roots:
        for path in text_files(root):
            if mode == "check":
                for number, line in remaining_lines(path):
                    total += 1
                    print("%s:%d: %s" % (path, number, line.strip()[:140]))
            else:
                with open(path, encoding="utf-8", errors="surrogateescape", newline="") as handle:
                    text = handle.read()
                fixed = fix_text(text, path)
                if fixed != text:
                    total += 1
                    with open(path, "w", encoding="utf-8", errors="surrogateescape", newline="") as handle:
                        handle.write(fixed)
                    print("fixed " + path)
    print("%s: %d %s" % (mode, total, "British lines found" if mode == "check" else "files rewritten"))
    return 1 if (mode == "check" and total) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
