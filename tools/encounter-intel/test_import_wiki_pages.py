import hashlib
import json
import pathlib
import tempfile
import unittest

from import_wiki_pages import import_pages


class PageImportTests(unittest.TestCase):
    def fixture(self, root):
        text = "== Move Pattern ==\nCycles through Butt, Hesitant Slice, Hiss.\n== Notes ==\nEditorial notes.\n"
        (root / "nibbit.wiki").write_text(text)
        page = {"name": "Nibbit", "file": "nibbit.wiki", "source_url": "https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2:Nibbit?oldid=123&action=edit",
                "revision": 123, "fetched_at": "2026-10-12T00:00:00Z", "sha256": hashlib.sha256(text.encode()).hexdigest()}
        receipt = {"schema": 1, "acquisition": "synthetic test source, not wiki evidence", "pages": [page]}
        (root / "sources.json").write_text(json.dumps(receipt))
        return receipt

    def test_available_text_keeps_revision_hash_and_pattern_without_evaluating_it(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self.fixture(root)
            page = import_pages(root)["pages"][0]
            self.assertEqual("Cycles through Butt, Hesitant Slice, Hiss.", page["pattern_text"])
            self.assertEqual(123, page["source"]["revision"])
            self.assertIn("Editorial notes.", page["source_text"])

    def test_empty_acquisition_is_explicit_not_a_successful_scrape(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "sources.json").write_text(json.dumps({"schema": 1, "acquisition": "unavailable: challenge", "pages": []}))
            result = import_pages(root)
            self.assertEqual([], result["pages"])
            self.assertIn("unavailable", result["acquisition"])

    def test_changed_hash_revision_identity_and_escaping_paths_refuse(self):
        for field, value in [("sha256", "0" * 64), ("revision", 124), ("file", "../escape.wiki"),
                             ("source_url", "https://example.com/wiki/Slay_the_Spire_2:Nibbit?oldid=123&action=edit")]:
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                root = pathlib.Path(directory)
                receipt = self.fixture(root)
                receipt["pages"][0][field] = value
                (root / "sources.json").write_text(json.dumps(receipt))
                with self.assertRaises(ValueError):
                    import_pages(root)

    def test_literal_cycles_sequences_openings_and_numeric_claims_keep_source_lines(self):
        from import_wiki_pages import claims_of, section
        text = "Lead\n== Pattern ==\nAlways cycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} and repeats.\nOpens with {{Int2|Test|Attack}}.\n{{Intents Table/row\n| Name = Attack\n| Effect = Deals (5;{{Asc2|9|7}}) damage 2 times.\n}}\n== Notes ==\nNot a pattern.\n"
        pattern, offset = section(text)
        claims = claims_of(text, 'Test', pattern, offset)
        self.assertEqual(['fixed-cycle', 'opening-alternative', 'damage', 'hits'], [claim['kind'] for claim in claims])
        self.assertEqual(3, claims[0]['source_line'])
        self.assertEqual({'base': 5, 'threshold': 9, 'higher': 7}, claims[2]['amount'])
        for claim in claims:
            self.assertIn(claim['source_text'], text)
        after_table = text.replace('Always cycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} and repeats.\n', '').replace('== Notes ==', 'Always cycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} and repeats.\n== Notes ==')
        pattern, offset = section(after_table)
        cycle = next(claim for claim in claims_of(after_table, 'Test', pattern, offset) if claim['kind'] == 'fixed-cycle')
        self.assertEqual(after_table.splitlines().index(cycle['source_text']) + 1, cycle['source_line'])
        listed = "== Pattern ==\nAlternates in this fixed pattern:\n# {{Int2|Test|Attack}}.\n# {{Int2|Test|Buff}}.\n# Back to {{Int2|Test|Attack}}.\n"
        pattern, offset = section(listed)
        self.assertEqual(['Test.Attack', 'Test.Buff'], claims_of(listed, 'Test', pattern, offset)[0]['moves'])

    def test_cycle_clauses_do_not_include_repeated_opening_or_effect_references(self):
        from import_wiki_pages import claims_of, section
        text = '== Pattern ==\nAlternates between {{Int2|Test|Attack}} and {{Int2|Test|Buff}}, starting with {{Int2|Test|Attack}}. Because {{Int2|Test|Buff}} gains Strength, {{Int2|Test|Attack}} grows.\nUses {{Int2|Test|Attack}} every turn while its ally is alive.\nUses {{Int2|Test|Explode}} every turn, killing itself.\nRepeats a fixed three-turn cycle:\n# {{Int2|Test|Attack}}\n# {{Int2|Test|Buff}}\n# {{Int2|Test|Attack}}\n'
        pattern, offset = section(text)
        claims = claims_of(text, 'Test', pattern, offset)
        self.assertEqual(2, len(claims))
        self.assertEqual(['Test.Attack', 'Test.Buff'], claims[0]['moves'])
        self.assertEqual(['Test.Attack', 'Test.Buff', 'Test.Attack'], claims[1]['moves'])

    def test_random_qualified_cycles_and_complex_amounts_are_not_guessed(self):
        from import_wiki_pages import claims_of, section
        text = "== Pattern ==\nCycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} if healthy.\n{{Intents Table/row\n| Name = Attack\n| Effect = Deals (3x3;{{Asc2|9|4x3}}) damage.\n}}\n"
        pattern, offset = section(text)
        self.assertEqual([], claims_of(text, 'Test', pattern, offset))

    def test_cached_redirect_resolves_to_section_with_both_provenance_receipts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            receipt = self.fixture(root)
            redirect = '#REDIRECT [[sts2:Group#Nibbit]]\n'
            (root / 'nibbit.wiki').write_text(redirect)
            receipt['pages'][0]['sha256'] = hashlib.sha256(redirect.encode()).hexdigest()
            target = "== Nibbit ==\nCycles {{Int2|Nibbit|Butt}} then {{Int2|Nibbit|Slice}}.\n== Other ==\nOpens with {{Int2|Other|Attack}}.\n"
            (root / 'group.wiki').write_text(target)
            receipt['pages'].append(dict(receipt['pages'][0], name='Group', file='group.wiki', sha256=hashlib.sha256(target.encode()).hexdigest(), source_url='https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2:Group?oldid=123&action=edit'))
            (root / 'sources.json').write_text(json.dumps(receipt))
            page = import_pages(root)['pages'][0]
            self.assertEqual('enemy-page:Group', page['source']['module'])
            self.assertEqual('enemy-page:Nibbit', page['redirect_sources'][0]['module'])
            self.assertNotIn('Other', page['pattern_text'])
            self.assertEqual(['Nibbit.Butt', 'Nibbit.Slice'], page['claims'][0]['moves'])
            receipt['pages'].pop()
            (root / 'sources.json').write_text(json.dumps(receipt))
            self.assertIn('Redirect target', import_pages(root)['pages'][0]['gaps'][0])

    def test_unknown_schema_and_duplicate_sources_refuse(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            receipt = self.fixture(root)
            receipt["pages"] *= 2
            (root / "sources.json").write_text(json.dumps(receipt))
            with self.assertRaises(ValueError):
                import_pages(root)
            receipt["schema"] = 2
            (root / "sources.json").write_text(json.dumps(receipt))
            with self.assertRaises(ValueError):
                import_pages(root)


if __name__ == "__main__":
    unittest.main()
