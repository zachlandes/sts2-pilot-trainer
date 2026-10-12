#!/usr/bin/env python3
"""Import saved, pinned wiki modules without a network client or Lua evaluator."""
import argparse
import hashlib
import json
from pathlib import Path
import re


class LiteralParser:
    def __init__(self, text):
        self.text = text
        self.position = 0

    def skip(self):
        while self.position < len(self.text):
            if self.text[self.position].isspace():
                self.position += 1
            elif self.text.startswith('--', self.position):
                end = self.text.find('\n', self.position)
                self.position = len(self.text) if end < 0 else end + 1
            else:
                break

    def take(self, token):
        self.skip()
        if not self.text.startswith(token, self.position):
            raise ValueError(f'Expected {token!r} at offset {self.position}')
        self.position += len(token)

    def value(self):
        self.skip()
        char = self.text[self.position:self.position + 1]
        if char == '{':
            return self.table()
        if char in ('"', "'"):
            self.position += 1
            result = []
            while self.position < len(self.text):
                current = self.text[self.position]
                self.position += 1
                if current == char:
                    return ''.join(result)
                if current == '\\':
                    escaped = self.text[self.position:self.position + 1]
                    escapes = {'n': '\n', 'r': '\r', 't': '\t', '\\': '\\', '"': '"', "'": "'"}
                    if escaped not in escapes:
                        raise ValueError(f'Unknown string escape at {self.position}')
                    self.position += 1
                    current = escapes[escaped]
                result.append(current)
            raise ValueError('Unclosed string')
        match = re.match(r'-?\d+(?:\.\d+)?\b|true\b|false\b', self.text[self.position:])
        if not match:
            raise ValueError(f'Non-literal expression at offset {self.position}')
        self.position += len(match[0])
        return json.loads(match[0])

    def table(self):
        self.take('{')
        named, sequence = {}, []
        while True:
            self.skip()
            if self.text.startswith('}', self.position):
                self.position += 1
                if named and sequence:
                    raise ValueError('Mixed tables are unsupported')
                return named if named else sequence
            key = None
            if self.text.startswith('[', self.position):
                self.take('[')
                key = self.value()
                self.take(']')
                self.take('=')
            else:
                match = re.match(r'([A-Za-z_][A-Za-z_0-9]*)\s*=', self.text[self.position:])
                if match:
                    key = match[1]
                    self.position += len(match[0])
            value = self.value()
            if key is None:
                sequence.append(value)
            else:
                if not isinstance(key, str) or key in named:
                    raise ValueError(f'Invalid or duplicate table key {key!r}')
                named[key] = value
            self.skip()
            if self.text[self.position:self.position + 1] in (',', ';'):
                self.position += 1
            elif not self.text.startswith('}', self.position):
                raise ValueError(f'Missing table separator at offset {self.position}')


def parse_module(raw, module):
    parser = LiteralParser(raw)
    parser.take('local all_data =')
    data = parser.value()
    if not isinstance(data, dict):
        raise ValueError('Enemy module must contain a named literal table')
    # Only the documented editorial link wrapper may follow the literal data
    suffix = re.sub(r'\s+', ' ', raw[parser.position:]).strip()
    expected = ('local formatted = {} for name, enemy in pairs(all_data) do '
                f'enemy.EditLink = "{module.replace(" ", "_")}" '
                'formatted[name] = enemy end return formatted')
    if suffix != expected:
        raise ValueError(f'Unrecognized module wrapper: {module}')
    return data


QUANTITY = r'(?P<base>\d+)(?: \(\{\{Asc2\|(?P<tier>\d+)\|(?P<higher>\d+)\}\}\))?'


def move_quantities(text):
    """Extract only unambiguous reference quantities, not arbitrary effect semantics."""
    patterns = {
        'damage': r'Deals ' + QUANTITY + r' damage',
        'block': r'Gains ' + QUANTITY + r' \{\{KW2\|Block\}\}',
        'strength': r'Gains ' + QUANTITY + r' \{\{BD2\|Strength\}\}',
        'hits': (r'damage [×x](?P<base>\d+)'
                 r'(?: \(\{\{Asc2\|(?P<tier>\d+)\|[×x](?P<higher>\d+)\}\}\))?'
                 r'(?!\d| \(\{\{Asc2)'),
    }
    result = []
    for kind, pattern in patterns.items():
        for match in re.finditer(pattern, text):
            groups = match.groupdict()
            result.append(dict(kind=kind, offset=match.start(), text=match[0],
                               amount=dict(base=int(groups['base']),
                                           threshold=int(groups['tier']) if groups.get('tier') else None,
                                           higher=int(groups['higher']) if groups.get('higher') else None)))
    return sorted(result, key=lambda quantity: quantity['offset'])


def composition(location, text):
    if not isinstance(location, str) or not isinstance(text, str):
        raise ValueError('Encounter location and composition must be text')
    # Repeated icons retain multiplicity, but a group link is not a monster identity
    targets = re.findall(r'\[\[File:[^\]]*\|link=([^\]]+)\]\]', text)
    links = [re.sub(r'^(?:StS2|Slay the Spire 2):', '', target) for target in targets]
    if not links or any(link == target for link, target in zip(links, targets)):
        raise ValueError(f'Unrecognized icon links in encounter composition: {location}')
    return dict(location=location, text=text, evidence_kind='icon-row', members=[dict(wiki_link=link, count=links.count(link))
                for link in dict.fromkeys(links)], is_event=bool(re.search(r'\bEvent\b', location)),
                gaps=['Icon links are editorial composition references, not verified opening rosters.'])


def remember_composition(encounters, entry, name, title):
    key = (entry['location'], entry['text'], entry['evidence_kind'])
    if key not in encounters:
        identity = hashlib.sha256(('\0'.join(key)).encode()).hexdigest()[:16]
        encounters[key] = dict(id='wiki:' + identity, **entry, references=[])
    reference = dict(enemy_name=name, source_module=title)
    if reference not in encounters[key]['references']:
        encounters[key]['references'].append(reference)
    return encounters[key]['id']


def party_phases(text, origin='party'):
    """Retain authored spawn/death statements without inventing missing spawn mechanics."""
    result = []
    for line in re.split(r'<br>|\n', text or ''):
        if not re.search(r'\b(?:summons|summoned|revives)\b', line, re.IGNORECASE):
            continue
        result.append(dict(trigger='death' if re.search(r'\b(?:death|dies|revives)\b', line, re.IGNORECASE) else 'summon',
                           relation=('summoned-by' if re.search(r'summoned by', line, re.IGNORECASE)
                                     else 'revives' if re.search(r'\brevives\b', line, re.IGNORECASE) else 'summons'),
                           text=line, origin=origin, monster_names=re.findall(r'\{\{M\|([^|}]+)', line),
                           gaps=['Editorial spawn statement; counts, slots and prevention of combat end are not game-verified.']))
    return result


def load_bundle(directory):
    directory = Path(directory).resolve()
    receipt = json.loads((directory / 'sources.json').read_text())
    modules = receipt['modules']
    if len(modules) != 8:
        raise ValueError('Require root module and its seven delegates')
    sources, entries, titles = [], [], set()
    encounters = {}
    delegates = None
    for index, source in enumerate(modules):
        title = source['title'].replace(' ', '_')
        if title in titles:
            raise ValueError(f'Duplicate source module {title}')
        titles.add(title)
        path = (directory / source['file']).resolve()
        if not path.is_relative_to(directory):
            raise ValueError('Source file escapes bundle directory')
        raw_bytes = path.read_bytes()
        raw = raw_bytes.decode('utf-8')
        digest = hashlib.sha256(raw_bytes).hexdigest()
        if source['sha256'] != digest:
            raise ValueError(f'Source hash differs: {title}')
        revision = source['revision']
        if type(revision) is not int or revision <= 0:
            raise ValueError('Require a positive revision id')
        url = 'https://slaythespire.wiki.gg/wiki/' + title
        pinned = url + '?action=raw&oldid=' + str(revision)
        if source['url'] != url or source['source_url'] != pinned or not source.get('fetched_at'):
            raise ValueError('Source URL, revision or fetch receipt differs')
        sources.append(dict(module=title, url=url, revision=revision, fetched_at=source['fetched_at'], sha256=digest))
        if index == 0:
            if title != 'Module:Enemies/StS2_data':
                raise ValueError('Root module must be first')
            delegates = {s.replace(' ', '_') for s in re.findall(r"require\('([^']+)'\)", raw)}
            continue
        for name, enemy in parse_module(raw, title).items():
            if not isinstance(enemy, dict) or not isinstance(enemy.get('Intents', []), list):
                raise ValueError(f'Unknown enemy shape: {name}')
            moves = []
            for move in enemy.get('Intents', []):
                if (not isinstance(move, dict) or not isinstance(move.get('Text'), str) or not move['Text'].strip()
                        or (move.get('Name') is not None and not isinstance(move['Name'], str))
                        or any(not isinstance(move.get(key, []), list)
                               or any(not isinstance(item, str) for item in move.get(key, []))
                               for key in ('AscText', 'IntentIcons'))):
                    raise ValueError(f'Unknown move shape: {name}')
                moves.append(dict(name=move.get('Name'), text=move['Text'], asc_text=move.get('AscText', []),
                                  icons=move.get('IntentIcons', []), quantities=move_quantities(move['Text'])))
            references = []
            for encounter in enemy.get('Encounters', []):
                if not isinstance(encounter, dict) or set(encounter) != {'location', 'enemies'}:
                    raise ValueError(f'Unknown encounter shape: {name}')
                entry = composition(encounter['location'], encounter['enemies'])
                references.append(remember_composition(encounters, entry, name, title))
            if enemy.get('InPartyWith'):
                entry = dict(location=enemy.get('Debut') or 'Unspecified', text=enemy['InPartyWith'],
                             evidence_kind='party-description', members=[],
                             is_event='event' in enemy['InPartyWith'].lower(),
                             gaps=['Party prose describes alternatives or spawns; it is not a verified opening roster.'])
                references.append(remember_composition(encounters, entry, name, title))
            known = {'Type', 'BaseHP', 'AscHP', 'StartsWith', 'InPartyWith', 'Intents', 'Image', 'Icon', 'Encounters'}
            entries.append(dict(name=name, source_module=title, kind=enemy.get('Type'), base_hp=enemy.get('BaseHP'),
                                asc_hp=enemy.get('AscHP'), starts_with=enemy.get('StartsWith'),
                                in_party_with=enemy.get('InPartyWith'), moves=moves,
                                encounter_ids=references, phases=(party_phases(enemy.get('InPartyWith'))
                                    + [phase for move in moves for phase in party_phases(move['text'], move['name'] or 'unnamed move')]),
                                pattern=dict(text=None, gap='The pinned modules contain no move-pattern field.'),
                                gaps=[] if moves else ['The pinned wiki entry has no moveset (group or missing data).'],
                                additional_fields={k: v for k, v in enemy.items() if k not in known}))
    if delegates != titles - {'Module:Enemies/StS2_data'}:
        raise ValueError('Saved delegates differ from the root module')
    if len({e['name'] for e in entries}) != len(entries):
        raise ValueError('Duplicate enemy identity across modules')
    return dict(schema=2, generated_by='tools/encounter-intel/import_wiki.py',
                attribution=dict(creator='Slay the Spire Wiki contributors',
                                 source='https://slaythespire.wiki.gg/wiki/Module:Enemies/StS2_data',
                                 license='CC BY-SA 4.0', license_url='https://creativecommons.org/licenses/by-sa/4.0/',
                                 policy_url='https://slaythespire.wiki.gg/wiki/Meta:Copyrights',
                                 changes='Parsed literal Lua tables; normalized fields and numeric claims; grouped editorial encounter references; retained spawn statements and wikitext without executing or rendering it; omitted standalone art fields. No missing patterns or rosters were invented.'),
                sources=sources, enemies=sorted(entries, key=lambda e: e['name']),
                encounters=sorted(encounters.values(), key=lambda e: e['id']))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bundle', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    output = json.dumps(load_bundle(args.bundle), ensure_ascii=False, indent=2) + '\n'
    if args.check:
        if args.output.read_text() != output:
            raise SystemExit('Wiki catalogue differs from the receipted source bundle')
    else:
        args.output.write_text(output)


if __name__ == '__main__':
    main()
