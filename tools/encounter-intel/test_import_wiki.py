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
            lambda receipt, _: receipt['modules'][1].update(source_url='https://example.com/not-a-revision'),
            lambda receipt, _: receipt['modules'][1].update(file='../outside.lua'),
            lambda receipt, _: receipt['modules'][1].update(title=receipt['modules'][0]['title']),
        ]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
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
