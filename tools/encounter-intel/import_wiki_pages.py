"""Import cached enemy pages and bounded literal claims offline; never fetch or evaluate wikitext."""
import argparse
import fractions
import hashlib
import json
import math
import pathlib
import re
import urllib.parse

LINK = re.compile(r"\{\{Int2\|([^|}]+)\|([^|}]+)(?:\|[^}]*)?\}\}")
REDIRECT = re.compile(r"\s*#REDIRECT\s*\[\[(?:Slay[_ ]the[_ ]Spire[_ ]2|sts2):([^#\]]+)(?:#([^\]]+))?\]\]", re.I)
AMOUNT = r"(?:\d+|\(\d+;\{\{Asc2\|\d+\|\d+\}\}\))"
HEADER = re.compile(r"(?m)^(={2,6})\s*(.*?)\s*\1\s*$")
INFOBOX = re.compile(r"\{\{Enemy Infobox\s*\n\|\s*Name\s*=\s*([^\n]+)")
TABLE = re.compile(r"\{\{Intents Table/start\|([^|}]+)\}\}")
RANDOM = re.compile(r"\brandomly\b|\brandom (?:choice|pick)\b", re.I)
OPENING = re.compile(r"\bfirst turn\b|\bopens?\b|\bstarts?\b", re.I)
EQUAL = re.compile(r"\bequal(?:ly weighted| (?:chance|weight|probability))\b", re.I)
LINKED = r"\{\{Int2\|(?P<owner%s>[^|}]+)\|(?P<move%s>[^|}]+)(?:\|[^}]*)?\}\}"
SPECIFIC = re.compile(LINKED % ('', '') + r"\s+cannot be used (?:twice|two turns) in a row|\bcannot use " + LINKED % ('2', '2') + r" twice in a row", re.I)
SAME = re.compile(r"\b(?:cannot|can't)\s+(?:use|pick|be used|repeat)\s+the same (?:move|one)\s+(?:twice|two turns) in a row|\bno move can be used (?:twice|two turns) in a row", re.I)
FREE = re.compile(r"\bno restriction on repeating\b|\bcan repeat\b", re.I)
WEIGHT_WORDS = re.compile(r"\bchance\b|\bas likely\b|\bweight(?:ed|s)?\b|\(\d+%\)|\bequal(?:ly)? (?:chance|weight|probability|weighted|likely)\b|\bprobability\b", re.I)
REPEAT_WORDS = re.compile(r"\bin a row\b|\bconsecutive(?:ly)?\b|\brestriction\b|\bcan(?:not)? repeat\b", re.I)


def amount(text):
    if text.isdigit():
        return {"base": int(text), "threshold": None, "higher": None}
    match = re.fullmatch(r"\((\d+);\{\{Asc2\|(\d+)\|(\d+)\}\}\)", text)
    if not match:
        raise ValueError("Unrecognized literal amount")
    low, threshold, high = map(int, match.groups())
    return {"base": low, "threshold": threshold, "higher": high}


def identity(value):
    return re.sub(r"[^a-z0-9]", "", value.lower())


def is_pattern(title):
    return identity(title) in {"pattern", "movepattern", "behavior", "behaviour"}


def regions(text, name, anchor=None):
    """Every pattern region that belongs to the named enemy, with its offset into the page."""
    headers = list(HEADER.finditer(text))
    def body(index):
        header = headers[index]
        end = next((other.start() for other in headers[index + 1:] if len(other[1]) <= len(header[1])), len(text))
        raw = text[header.end():end]
        return raw.strip(), header.end() + len(raw) - len(raw.lstrip())
    if anchor is not None:
        index = next((index for index, header in enumerate(headers) if identity(header[2]) == identity(anchor.replace('_', ' '))), None)
        if index is None:
            return []
        found = [body(index)]
        level = len(headers[index][1])
        for later in range(index + 1, len(headers)):
            if len(headers[later][1]) > level:
                continue
            # Some pages put an anchored enemy's pattern in a same-level sibling section
            if len(headers[later][1]) == level and is_pattern(headers[later][2]):
                found.append(body(later))
                continue
            break
        return found
    owner, found = name, []
    for index, header in enumerate(headers):
        if is_pattern(header[2]):
            if identity(owner) == identity(name):
                found.append(body(index))
            continue
        end = headers[index + 1].start() if index + 1 < len(headers) else len(text)
        box = INFOBOX.search(text, header.end(), end)
        if box:
            owner = box[1].strip()
    return found


def claims_of(text, name, found):
    """Check only literal graph assertions; all qualifiers and remaining prose stay editorial."""
    claims = []
    def add(kind, moves, start, excerpt, number=None, **stated):
        claims.append({"kind": kind, "moves": moves, "source_line": text[:start].count('\n') + 1,
                       "source_text": excerpt, "amount": number, **stated})
    for pattern, pattern_offset in found:
        # Tables describe move effects, not transition order
        prose = re.sub(r"(?ms)^\{\{Intents Table/row\s*\n.*?^\}\}\s*$", lambda match: ''.join('\n' if char == '\n' else ' ' for char in match[0]), pattern)
        for match in re.finditer(r"(?m)^[^\n]*\b(?:cycles|alternates?)\b[^\n]*", prose, re.I):
            line = match[0]
            tail = re.split(r"\b(?:cycles|alternates?)\b", line, maxsplit=1, flags=re.I)[1]
            tail = re.split(r"\.\s+|\.$|,\s*(?:always\s+)?starting\s+(?:with|on)", tail, maxsplit=1, flags=re.I)[0]
            links = [key(link) for link in LINK.finditer(tail)]
            if len(links) >= 2 and not re.search(r"\b(?:or|either|random|chance|if|unless|until|times|twice)\b|%", LINK.sub('@', tail), re.I):
                add('fixed-cycle', links, pattern_offset + match.start(), line)
        for match in re.finditer(r"(?m)^([^\n]*)\n((?:\#[^\n]*(?:\n|$))+)", prose):
            prefix, rows = match[1], match[2]
            links = []
            for row in rows.splitlines():
                targets = list(LINK.finditer(row))
                if len(targets) != 1 or re.search(r"\b(?:or|either|times|twice|chance|random|if|unless|until)\b|%", LINK.sub('@', row), re.I):
                    links = []
                    break
                links.append(key(targets[0]))
            if len(links) >= 2 and re.search(r"\b(?:sequence|cycle|cycles|alternates|pattern|repeats)\b", prefix, re.I):
                if re.search(r"\b(?:or|either|chance|random|if|unless|until)\b|%", LINK.sub('@', prefix), re.I):
                    continue
                cycle = bool(re.search(r"\b(?:cycle|cycles|alternates|repeats)\b", prefix, re.I)) or links[0] == links[-1]
                if links[0] == links[-1] and re.search(r"\b(?:back to|again)\b", rows.splitlines()[-1], re.I):
                    links.pop()
                add('fixed-cycle' if cycle else 'fixed-path', links, pattern_offset + match.start(), match[0].strip())
        for match in re.finditer(r"(?m)^[^\n]*(?:\bopens?\b|\bstarts? with\b|\bon (?:the |its )?first turn\b)[^\n]*", prose, re.I):
            links = list(LINK.finditer(match[0]))
            if len(links) == 1:
                # An opening alternative is not a claim that its qualifier applies to every encounter
                add('opening-alternative', [key(links[0])], pattern_offset + match.start(), match[0])
        for match in re.finditer(r"(?m)^[^\n]*\{\{Int2\|[^\n]*\bevery turn\b[^\n]*", prose, re.I):
            links = list(LINK.finditer(match[0]))
            if len(links) == 1 and not re.search(r"\b(?:while|when|until|if|unless|dies|itself)\b", LINK.sub('@', match[0]), re.I):
                add('self-loop', [key(links[0])], pattern_offset + match.start(), match[0])
        branch_claims(prose, pattern_offset, add)
        # Retain exact table rows as provenance; complex formulas are not partially parsed
        for row in re.finditer(r"(?ms)^\{\{Intents Table/row\s*\n(.*?)^\}\}", pattern):
            move = re.search(r"(?m)^\|\s*Name\s*=\s*([^\n]+)", row[1])
            effect = re.search(r"(?m)^\|\s*Effect\s*=\s*([^\n]+)", row[1])
            if not move or not effect or '{' in move[1]:
                continue
            tables = [table for table in TABLE.finditer(pattern, 0, row.start())]
            target = (tables[-1][1].strip() if tables else name) + '.' + move[1].strip()
            damage = list(re.finditer(r"\bDeals (" + AMOUNT + r") damage\b", effect[1]))
            if len(damage) == 1:
                add('damage', [target], pattern_offset + row.start(), row[0], amount(damage[0][1]))
            hits = re.search(r"\bdamage (" + AMOUNT + r") times\b", effect[1])
            if hits:
                add('hits', [target], pattern_offset + row.start(), row[0], amount(hits[1]))
    return sorted(claims, key=lambda claim: claim['source_line'])


def key(link):
    return link[1].replace('_', ' ') + '.' + link[2].replace('_', ' ')


def branch_claims(prose, offset, add):
    """Odds and repetition rules a sentence states literally; vocabulary that binds to nothing stays unparsed."""
    def unique(links):
        moves = []
        for link in links:
            if key(link) not in moves:
                moves.append(key(link))
        return moves
    def scope_of(lead):
        return 'opening' if OPENING.search(lead) and not re.search(r"\b(?:then|after)\b", lead, re.I) else 'branch'
    lines = list(re.finditer(r"(?m)^[^\n]*", prose))
    context, scope, consumed = [], 'branch', set()
    for index, line in enumerate(lines):
        if index in consumed or not line[0].strip() or line[0].lstrip().startswith(('|', '}}', '{{Intents', '{{Enemy', '{{Power')):
            continue
        start = offset + line.start()
        chosen = RANDOM.search(LINK.sub('@', line[0]))
        if chosen and line[0].rstrip().endswith(':'):
            rows = []
            for later in lines[index + 1:]:
                row = re.fullmatch(r"\*\s*(\d+)% chance of " + LINK.pattern + r"\.?", later[0].strip())
                if not row:
                    break
                rows.append((later, int(row[1]), key(LINK.search(later[0]))))
            if len(rows) >= 2 and len({move for _, _, move in rows}) == len(rows):
                consumed.update(range(index + 1, index + 1 + len(rows)))
                context, scope = [move for _, _, move in rows], scope_of(LINK.sub('@', line[0])[:chosen.start()])
                add('weights', context, start, prose[line.start():rows[-1][0].end()], weights=[weight for _, weight, _ in rows], scope=scope)
                continue
        # Sentence breaks inside a move link are part of its name
        guarded = LINK.sub(lambda link: link[0].replace('.', '\0').replace('!', '\1').replace('?', '\2'), line[0])
        for sentence in re.split(r"(?<=[.!?])\s+", guarded):
            said = sentence.replace('\0', '.').replace('\1', '!').replace('\2', '?')
            relative = re.search(r",?\s*with " + LINK.pattern + r" being (twice|three times) as likely", said)
            listed = said if relative is None else said[:relative.start()] + said[relative.end():]
            plain = LINK.sub('@', listed)
            weighted = repeated = False
            chosen = RANDOM.search(plain)
            if chosen:
                links = [link for link in LINK.finditer(listed) if len(LINK.sub('@', listed[:link.start()])) >= chosen.start()]
                context, scope = unique(links), scope_of(plain[:chosen.start()])
                notes = [re.match(r"\s*\(([^)]*)\)", listed[link.end():]) for link in links]
                for link, note in zip(links, notes):
                    if note and re.fullmatch(r"\s*cannot repeat\s*", note[1], re.I):
                        add('repeat', [key(link)], start, line[0], repeat='CannotRepeat')
                        repeated = True
                odds = [stated_weight(note[1]) if note else None for note in notes]
                if len(context) == len(links) >= 2:
                    if all(weight is not None for weight in odds):
                        common = math.lcm(*(weight.denominator for weight in odds))
                        add('weights', context, start, line[0], weights=[int(weight * common) for weight in odds], scope=scope)
                        weighted = True
                    elif not any(notes[:-1]) and (notes[-1] is None or EQUAL.fullmatch(notes[-1][1].split(',')[0].strip())):
                        if relative and key(relative) in context:
                            factor = {'twice': 2, 'three times': 3}[relative[3]]
                            add('weights', context, start, line[0], weights=[factor if move == key(relative) else 1 for move in context], scope=scope)
                            weighted = True
                        elif EQUAL.search(plain):
                            add('weights', context, start, line[0], weights=[1] * len(context), scope=scope)
                            weighted = True
            elif EQUAL.search(plain) and not LINK.search(said) and len(context) >= 2:
                add('weights', context, start, line[0], weights=[1] * len(context), scope=scope)
                weighted = True
            for link in SPECIFIC.finditer(said):
                owner, move = link['owner'] or link['owner2'], link['move'] or link['move2']
                add('repeat', [owner.replace('_', ' ') + '.' + move.replace('_', ' ')], start, line[0], repeat='CannotRepeat')
                repeated = True
            general = 'CannotRepeat' if SAME.search(plain) else 'Unrestricted' if FREE.search(plain) else None
            if general and context:
                for move in context:
                    add('repeat', [move], start, line[0], repeat=general)
                repeated = True
            spoken = LINK.sub('@', said)
            if WEIGHT_WORDS.search(spoken) and not weighted or REPEAT_WORDS.search(spoken) and not repeated:
                add('unparsed', unique(LINK.finditer(said)), start, line[0])


def stated_weight(note):
    note = note.strip()
    for form, value in ((r"(\d+)/([1-9]\d*)(?: chance)?", lambda match: fractions.Fraction(int(match[1]), int(match[2]))),
                        (r"(\d+)%(?: chance)?", lambda match: fractions.Fraction(int(match[1]))),
                        (r"weight (\d+)", lambda match: fractions.Fraction(int(match[1])))):
        match = re.fullmatch(form, note, re.I)
        if match:
            return value(match)
    return None


def import_pages(directory):
    root = pathlib.Path(directory).resolve()
    receipt = json.loads((root / "sources.json").read_text())
    if set(receipt) not in ({"schema", "acquisition", "pages"}, {"schema", "acquisition", "pages", "unavailable_pages"}) or receipt["schema"] != 1:
        raise ValueError("Unknown page receipt schema")
    if not isinstance(receipt["acquisition"], str) or not receipt["acquisition"]:
        raise ValueError("Missing acquisition disposition")
    saved = {}
    for page in receipt["pages"]:
        if page["name"] in saved:
            raise ValueError("Duplicate enemy page")
        revision = page["revision"]
        if not isinstance(revision, int) or isinstance(revision, bool) or revision <= 0:
            raise ValueError("Missing source revision")
        url = urllib.parse.urlsplit(page["source_url"])
        expected = "/wiki/" + urllib.parse.quote("Slay_the_Spire_2:" + page["name"].replace(" ", "_"), safe=":")
        if url.scheme != "https" or url.netloc != "slaythespire.wiki.gg" or url.path != expected or url.fragment:
            raise ValueError("Source is not the named enemy page")
        query = urllib.parse.parse_qs(url.query)
        if query.get("oldid") != [str(revision)] or query.get("action") not in (["edit"], ["raw"]):
            raise ValueError("Source revision URL disagrees with receipt")
        path = (root / page["file"]).resolve()
        if path.parent != root or path.suffix != ".wiki":
            raise ValueError("Source path escapes the page bundle")
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != page["sha256"]:
            raise ValueError("Source hash disagrees with receipt")
        saved[page["name"]] = (page, raw.decode("utf-8"))
    def source(page):
        return {"module": "enemy-page:" + page["name"], "url": page["source_url"], "revision": page["revision"],
                "fetched_at": page["fetched_at"], "sha256": page["sha256"]}
    def resolve(name, seen=None):
        seen = seen or set()
        if name in seen:
            raise ValueError("Redirect cycle in cached pages")
        page, text = saved[name]
        redirect = REDIRECT.match(text)
        if redirect:
            target = redirect[1].replace('_', ' ')
            if target in saved:
                final, body, anchor, references = resolve(target, seen | {name})
                return final, body, redirect[2] or anchor, [source(page)] + references
            return page, text, None, []
        return page, text, None, []
    pages = []
    for name in saved:
        page, text, anchor, redirects = resolve(name)
        unresolved_redirect = bool(REDIRECT.match(text))
        found = [] if unresolved_redirect else regions(text, name, anchor)
        gaps = ["Redirect target was not cached; this is a redirect receipt, not enemy fallback text."] if unresolved_redirect else (
            (["Redirect anchor was not recognized; target text is retained without interpreted claims."] if anchor and not found else []) +
            (["No pattern section is owned by this page's enemy; the whole page is retained as fallback text."] if not anchor and not found else []) +
            ["Only listed literal claims are checked; unparsed odds or repetition prose, predicate qualifiers, reactive triggers and remaining effect prose are not verified."])
        pages.append({"name": name, "source": source(page), "source_text": text,
                      "pattern_regions": [{"source_line": text[:offset].count('\n') + 1, "text": region} for region, offset in found],
                      "redirect_sources": redirects, "claims": claims_of(text, name, found), "gaps": gaps})
    return {"schema": 1, "acquisition": receipt["acquisition"], "pages": pages, "unavailable_pages": receipt.get("unavailable_pages", [])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    content = json.dumps(import_pages(args.bundle), ensure_ascii=False, indent=2) + "\n"
    if args.check:
        if args.output.read_text() != content:
            raise SystemExit("Cached enemy-page reference is stale")
    else:
        args.output.write_text(content)


if __name__ == "__main__":
    main()
