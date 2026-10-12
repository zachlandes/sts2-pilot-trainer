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
