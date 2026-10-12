"""Import cached enemy pages and bounded literal claims offline; never fetch or evaluate wikitext."""
import argparse
import hashlib
import json
import pathlib
import re
import urllib.parse

LINK = re.compile(r"\{\{Int2\|([^|}]+)\|([^|}]+)(?:\|[^}]*)?\}\}")
REDIRECT = re.compile(r"\s*#REDIRECT\s*\[\[(?:Slay[_ ]the[_ ]Spire[_ ]2|sts2):([^#\]]+)(?:#([^\]]+))?\]\]", re.I)
AMOUNT = r"(?:\d+|\(\d+;\{\{Asc2\|\d+\|\d+\}\}\))"


def amount(text):
    if text.isdigit():
        return {"base": int(text), "threshold": None, "higher": None}
    match = re.fullmatch(r"\((\d+);\{\{Asc2\|(\d+)\|(\d+)\}\}\)", text)
    if not match:
        raise ValueError("Unrecognized literal amount")
    low, threshold, high = map(int, match.groups())
    return {"base": low, "threshold": threshold, "higher": high}


def section(text, anchor=None):
    headers = list(re.finditer(r"(?m)^(={2,6})\s*(.*?)\s*\1\s*$", text))
    identity = lambda value: re.sub(r"[^a-z0-9]", "", value.lower())
    for index, header in enumerate(headers):
        if anchor is not None:
            wanted = identity(header[2]) == identity(anchor.replace('_', ' '))
        else:
            wanted = identity(header[2]) in {"pattern", "movepattern", "behavior", "behaviour"}
        if wanted:
            end = next((other.start() for other in headers[index + 1:] if len(other[1]) <= len(header[1])), len(text))
            return text[header.end():end].strip(), header.end() + len(text[header.end():end]) - len(text[header.end():end].lstrip())
    return None, 0


def claims_of(text, name, pattern, pattern_offset):
    """Check only literal graph assertions; all qualifiers and remaining prose stay editorial."""
    claims = []
    def add(kind, moves, start, excerpt, number=None):
        claims.append({"kind": kind, "moves": moves, "source_line": text[:start].count('\n') + 1,
                       "source_text": excerpt, "amount": number})
    if pattern:
        # Tables describe move effects, not transition order
        prose = re.sub(r"(?ms)^\{\{Intents Table/row\s*\n.*?^\}\}\s*$", lambda match: ''.join('\n' if char == '\n' else ' ' for char in match[0]), pattern)
        for match in re.finditer(r"(?m)^[^\n]*\b(?:cycles|alternates)\b[^\n]*", prose, re.I):
            line = match[0]
            tail = re.split(r"\b(?:cycles|alternates)\b", line, maxsplit=1, flags=re.I)[1]
            tail = re.split(r"\.\s+|\.$|,\s*(?:always\s+)?starting\s+(?:with|on)", tail, maxsplit=1, flags=re.I)[0]
            links = [owner.replace('_', ' ') + '.' + move.replace('_', ' ') for owner, move in LINK.findall(tail)]
            if len(links) >= 2 and not re.search(r"\b(?:or|either|random|chance|if|unless|until|times|twice)\b|%", LINK.sub('@', tail), re.I):
                add('fixed-cycle', links, pattern_offset + match.start(), line)
        for match in re.finditer(r"(?m)^([^\n]*)\n((?:\#[^\n]*(?:\n|$))+)", prose):
            prefix, rows = match[1], match[2]
            links = []
            for row in rows.splitlines():
                targets = LINK.findall(row)
                if len(targets) != 1 or re.search(r"\b(?:or|either|times|twice|chance|random|if|unless|until)\b|%", LINK.sub('@', row), re.I):
                    links = []
                    break
                links.append(targets[0][0].replace('_', ' ') + '.' + targets[0][1].replace('_', ' '))
            if len(links) >= 2 and re.search(r"\b(?:sequence|cycle|cycles|alternates|pattern|repeats)\b", prefix, re.I):
                if re.search(r"\b(?:or|either|chance|random|if|unless|until)\b|%", LINK.sub('@', prefix), re.I):
                    continue
                cycle = bool(re.search(r"\b(?:cycle|cycles|alternates|repeats)\b", prefix, re.I)) or links[0] == links[-1]
                if links[0] == links[-1] and re.search(r"\b(?:back to|again)\b", rows.splitlines()[-1], re.I):
                    links.pop()
                add('fixed-cycle' if cycle else 'fixed-path', links, pattern_offset + match.start(), match[0].strip())
        for match in re.finditer(r"(?m)^[^\n]*(?:\bopens?\b|\bstarts? with\b|\bon (?:the |its )?first turn\b)[^\n]*", prose, re.I):
            links = LINK.findall(match[0])
            if len(links) == 1:
                owner, move = links[0]
                # An opening alternative is not a claim that its qualifier applies to every encounter
                add('opening-alternative', [owner.replace('_', ' ') + '.' + move.replace('_', ' ')], pattern_offset + match.start(), match[0])
        for match in re.finditer(r"(?m)^[^\n]*\{\{Int2\|[^\n]*\bevery turn\b[^\n]*", prose, re.I):
            links = LINK.findall(match[0])
            if len(links) == 1 and not re.search(r"\b(?:while|when|until|if|unless|dies|itself)\b", LINK.sub('@', match[0]), re.I):
                owner, move = links[0]
                add('self-loop', [owner.replace('_', ' ') + '.' + move.replace('_', ' ')], pattern_offset + match.start(), match[0])
    # Retain exact table rows as provenance; complex formulas are not partially parsed
    for row in re.finditer(r"(?ms)^\{\{Intents Table/row\s*\n(.*?)^\}\}", pattern or ''):
        move = re.search(r"(?m)^\|\s*Name\s*=\s*([^\n]+)", row[1])
        effect = re.search(r"(?m)^\|\s*Effect\s*=\s*([^\n]+)", row[1])
        if not move or not effect or '{' in move[1]:
            continue
        target = name + '.' + move[1].strip()
        damage = list(re.finditer(r"\bDeals (" + AMOUNT + r") damage\b", effect[1]))
        if len(damage) == 1:
            add('damage', [target], pattern_offset + row.start(), row[0], amount(damage[0][1]))
        hits = re.search(r"\bdamage (" + AMOUNT + r") times\b", effect[1])
        if hits:
            add('hits', [target], pattern_offset + row.start(), row[0], amount(hits[1]))
    return sorted(claims, key=lambda claim: claim['source_line'])


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
        pattern, offset = section(text, anchor)
        unresolved_redirect = bool(REDIRECT.match(text))
        pages.append({"name": name, "source": source(page), "source_text": text, "pattern_text": None if unresolved_redirect else pattern,
                      "redirect_sources": redirects, "claims": [] if unresolved_redirect else claims_of(text, name, pattern, offset),
                      "gaps": ["Redirect target was not cached; this is a redirect receipt, not enemy fallback text."] if unresolved_redirect else
                      (["Redirect anchor was not recognized; target text is retained without interpreted claims."] if anchor and pattern is None else []) +
                      ["Only listed literal claims are checked; predicate qualifiers, reactive triggers and remaining effect prose are not verified."]})
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
