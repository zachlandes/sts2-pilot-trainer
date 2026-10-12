import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).with_name('import_wiki.py')
SPEC = importlib.util.spec_from_file_location('import_wiki', MODULE)
IMPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(IMPORTER)
BUNDLE = MODULE.parents[2] / 'src/EncounterIntel/catalogue/wiki-sources'
OUTPUT = BUNDLE.parent / 'wiki-enemies.json'


class ImportTests(unittest.TestCase):
    def test_pinned_bundle_reproduces_committed_catalogue(self):
        result = IMPORTER.load_bundle(BUNDLE)
        self.assertEqual(json.loads(OUTPUT.read_text()), result)
        by_name = {enemy['name']: enemy for enemy in result['enemies']}
        self.assertIn('Hesitant Slice', [move['name'] for move in by_name['Nibbit']['moves']])
        self.assertIn('×4', by_name['Phrog Parasite']['moves'][1]['text'])
        self.assertIn('Rage', [move['name'] for move in by_name['Sludge Spinner']['moves']])
        self.assertEqual('CC BY-SA 4.0', result['attribution']['license'])
        self.assertEqual(50311, next(source['revision'] for source in result['sources'] if source['module'].endswith('/Underdocks')))

    def test_typed_encounters_phases_patterns_and_numeric_claims_are_preserved(self):
        result = IMPORTER.load_bundle(BUNDLE)
        enemies = {enemy['name']: enemy for enemy in result['enemies']}
        self.assertEqual(109, len(enemies))
        encounters = {encounter['id']: encounter for encounter in result['encounters']}
        for enemy in enemies.values():
            self.assertTrue(enemy['moves'] or enemy['gaps'])
            self.assertIsNotNone(enemy['pattern']['gap'])
            for identity in enemy['encounter_ids']:
                self.assertIn(dict(enemy_name=enemy['name'], source_module=enemy['source_module']),
                              encounters[identity]['references'])
        vegetation = [entry for entry in encounters.values()
                      if 'Dense Vegetation' in entry['location'] and entry['evidence_kind'] == 'icon-row']
        self.assertEqual(1, len(vegetation))
        self.assertTrue(vegetation[0]['is_event'])
        self.assertEqual([dict(wiki_link='Wriggler', count=4)], vegetation[0]['members'])
        self.assertEqual('death', enemies['Phrog Parasite']['phases'][0]['trigger'])
        self.assertEqual('summoned-by', enemies['Sneaky Gremlin']['phases'][0]['relation'])
        self.assertTrue(enemies['Knight Gang']['gaps'])
        self.assertEqual(dict(base=3, threshold=9, higher=4),
                         enemies['Exoskeleton']['moves'][0]['quantities'][1]['amount'])

    def test_both_wiki_namespace_spellings_keep_scroll_multiplicity(self):
        result = IMPORTER.load_bundle(BUNDLE)
        scrolls = sorted(member['count'] for entry in result['encounters'] if entry['evidence_kind'] == 'icon-row'
                         for member in entry['members'] if member['wiki_link'] == 'Scroll of Biting'
                         and len(entry['members']) == 1)
        self.assertEqual([3, 4], scrolls)
        for entry in result['encounters']:
            if entry['evidence_kind'] == 'icon-row':
                with self.subTest(location=entry['location'], text=entry['text']):
                    self.assertTrue(entry['members'])
                    self.assertTrue(entry['gaps'])
        self.assertEqual([dict(wiki_link='Scroll of Biting', count=2)], IMPORTER.composition(
            'Normal Encounter', '[[File:A.png|link=StS2:Scroll of Biting]][[File:A.png|link=Slay the Spire 2:Scroll of Biting]]')['members'])

    def test_icon_rows_with_unrecognized_or_missing_links_refuse(self):
        for text in ['[[File:A.png|link=Other Game:Scroll of Biting]]', '[[File:Summon Divider Icon.png]]', 'Scroll of Biting']:
            with self.subTest(text=text), self.assertRaisesRegex(ValueError, 'icon links'):
                IMPORTER.composition('Normal Encounter', text)

    def test_complex_or_unknown_quantities_are_not_partially_claimed_as_constants(self):
        self.assertEqual([], IMPORTER.move_quantities('Deals 5x2 ({{Asc2|9|6x2}}) damage.'))
        self.assertEqual([], IMPORTER.move_quantities('Deals 3/6({{Asc2|9|4/8}}) damage.'))
        self.assertEqual([], IMPORTER.move_quantities('damage x3 ({{Asc2|9|variable}})'))
        claims = IMPORTER.move_quantities('Deals 2 damage. Gains 3 {{BD2|Strength}}.')
        self.assertEqual(['damage', 'strength'], [claim['kind'] for claim in claims])
        self.assertEqual([0, 16], [claim['offset'] for claim in claims])

    def test_literal_parser_preserves_strings_and_tables(self):
        parser = IMPORTER.LiteralParser('{["enemy"]={Name="Quote: \\\"ok\\\"", Values={1,2}, Flag=true}, -- comment\n}')
        self.assertEqual({'enemy': {'Name': 'Quote: "ok"', 'Values': [1, 2], 'Flag': True}}, parser.value())

    def test_executable_expressions_and_mixed_or_duplicate_tables_refuse(self):
        for raw in ['{Attack=os.execute("bad")}', '{["x"]=1,["x"]=2}', '{Name="x", 2}', '{Text="x" Text="y"}', '{Text="\\q"}']:
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                IMPORTER.LiteralParser(raw).value()

    def test_unknown_postprocessing_refuses(self):
        raw = (BUNDLE / 'Module_Enemies_StS2_data_Overgrowth.lua').read_text()
        with self.assertRaisesRegex(ValueError, 'wrapper'):
            IMPORTER.parse_module(raw + '\nos.execute("not run")', 'Module:Enemies/StS2_data/Overgrowth')

    def changed_bundle(self, change):
        receipt = json.loads((BUNDLE / 'sources.json').read_text())
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            for source in receipt['modules']:
                (directory / source['file']).write_bytes((BUNDLE / source['file']).read_bytes())
            change(receipt, directory)
            (directory / 'sources.json').write_text(json.dumps(receipt))
            return IMPORTER.load_bundle(directory)

    def test_hash_revision_url_and_completeness_are_required(self):
        changes = [
            lambda receipt, _: receipt['modules'].pop(),
            lambda receipt, _: receipt['modules'][1].update(sha256='0' * 64),
            lambda receipt, _: receipt['modules'][1].update(revision=0),
            lambda receipt, _: receipt['modules'][1].update(revision=True),
            lambda receipt, _: receipt['modules'][1].update(source_url='https://example.com/not-a-revision'),
            lambda receipt, _: receipt['modules'][1].update(file='../outside.lua'),
            lambda receipt, _: receipt['modules'][1].update(title=receipt['modules'][0]['title']),
        ]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.changed_bundle(change)

    def test_malformed_move_fields_refuse_even_with_a_receipted_hash(self):
        def change(receipt, directory):
            source = receipt['modules'][1]
            path = directory / source['file']
            path.write_text(path.read_text().replace('IntentIcons = { "Attack3" }', 'IntentIcons = 1', 1))
            source['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
        with self.assertRaisesRegex(ValueError, 'move shape'):
            self.changed_bundle(change)

    def test_delegates_are_reconciled_even_when_changed_root_has_a_valid_hash(self):
        def change(receipt, directory):
            root = receipt['modules'][0]
            path = directory / root['file']
            path.write_text(path.read_text().replace('/Events', '/Stranger'))
            root['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
        with self.assertRaisesRegex(ValueError, 'delegates'):
            self.changed_bundle(change)


if __name__ == '__main__':
    unittest.main()
