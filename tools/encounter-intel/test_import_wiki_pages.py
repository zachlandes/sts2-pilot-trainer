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
            self.assertEqual([{"source_line": 2, "text": "Cycles through Butt, Hesitant Slice, Hiss."}], page["pattern_regions"])
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
        from import_wiki_pages import claims_of, regions
        text = "Lead\n== Pattern ==\nAlways cycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} and repeats.\nOpens with {{Int2|Test|Attack}}.\n{{Intents Table/row\n| Name = Attack\n| Effect = Deals (5;{{Asc2|9|7}}) damage 2 times.\n}}\n== Notes ==\nNot a pattern.\n"
        claims = claims_of(text, 'Test', regions(text, 'Test'))
        self.assertEqual(['fixed-cycle', 'opening-alternative', 'damage', 'hits'], [claim['kind'] for claim in claims])
        self.assertEqual(3, claims[0]['source_line'])
        self.assertEqual({'base': 5, 'threshold': 9, 'higher': 7}, claims[2]['amount'])
        for claim in claims:
            self.assertIn(claim['source_text'], text)
        after_table = text.replace('Always cycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} and repeats.\n', '').replace('== Notes ==', 'Always cycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} and repeats.\n== Notes ==')
        cycle = next(claim for claim in claims_of(after_table, 'Test', regions(after_table, 'Test')) if claim['kind'] == 'fixed-cycle')
        self.assertEqual(after_table.splitlines().index(cycle['source_text']) + 1, cycle['source_line'])
        listed = "== Pattern ==\nAlternates in this fixed pattern:\n# {{Int2|Test|Attack}}.\n# {{Int2|Test|Buff}}.\n# Back to {{Int2|Test|Attack}}.\n"
        self.assertEqual(['Test.Attack', 'Test.Buff'], claims_of(listed, 'Test', regions(listed, 'Test'))[0]['moves'])

    def test_cycle_clauses_do_not_include_repeated_opening_or_effect_references(self):
        from import_wiki_pages import claims_of, regions
        text = '== Pattern ==\nAlternates between {{Int2|Test|Attack}} and {{Int2|Test|Buff}}, starting with {{Int2|Test|Attack}}. Because {{Int2|Test|Buff}} gains Strength, {{Int2|Test|Attack}} grows.\nUses {{Int2|Test|Attack}} every turn while its ally is alive.\nUses {{Int2|Test|Explode}} every turn, killing itself.\nRepeats a fixed three-turn cycle:\n# {{Int2|Test|Attack}}\n# {{Int2|Test|Buff}}\n# {{Int2|Test|Attack}}\n'
        claims = claims_of(text, 'Test', regions(text, 'Test'))
        self.assertEqual(2, len(claims))
        self.assertEqual(['Test.Attack', 'Test.Buff'], claims[0]['moves'])
        self.assertEqual(['Test.Attack', 'Test.Buff', 'Test.Attack'], claims[1]['moves'])

    def test_random_qualified_cycles_and_dynamic_amounts_are_not_guessed(self):
        from import_wiki_pages import claims_of, regions
        text = "== Pattern ==\nCycles {{Int2|Test|Attack}} then {{Int2|Test|Buff}} if healthy.\n{{Intents Table/row\n| Name = Attack\n| Effect = Deals X damage, where X is its Strength.\n}}\n"
        self.assertEqual([], claims_of(text, 'Test', regions(text, 'Test')))
        conditional = "== Pattern ==\nFollows a repeating cycle: {{Int2|Test|Attack}}, {{Int2|Test|Buff}}, then a conditional check.\nThen the cycle continues with {{Int2|Test|Attack}} and {{Int2|Test|Buff}}.\n"
        self.assertEqual([], claims_of(conditional, 'Test', regions(conditional, 'Test')))

    def test_literal_multiplied_attacks_become_damage_and_hit_claims(self):
        from import_wiki_pages import claims_of, regions
        def quantities(effect):
            text = "== Pattern ==\n{{Intents Table/row\n|Name = Attack\n|Effect = " + effect + "\n}}\n"
            return [(claim['kind'], claim['amount']) for claim in claims_of(text, 'Test', regions(text, 'Test'))]
        def at(base, threshold=None, higher=None):
            return {'base': base, 'threshold': threshold, 'higher': higher}
        self.assertEqual([('damage', at(3, 9, 4)), ('hits', at(3, 9, 3))], quantities("Deals (3x3;{{Asc2|9|4x3}}) damage."))
        self.assertEqual([('damage', at(4, 9, 5)), ('hits', at(4))], quantities("Deals (4;{{Asc2|9|5}}) damage x4.}}"))
        self.assertEqual([('damage', at(7, 9, 8)), ('hits', at(3))], quantities("Deals (7;{{Asc2|9|8}}) damage ×3."))
        self.assertEqual([('damage', at(3)), ('hits', at(7, 9, 8))], quantities("Deals 3 damage (×7;{{Asc2|9|×8}})."))
        self.assertEqual([('damage', at(10, 9, 11)), ('hits', at(3))], quantities("Deals (10;{{Asc2|9|11}}) damage x3 (increases by 1 hit each use)."))
        self.assertEqual([('damage', at(8, 9, 10))], quantities("Deal (8;{{Asc2|9|10}}) damage. Gains (20;{{Asc2|8|22}}) {{KW2|Block}}."))
        self.assertEqual([('damage', at(5, 9, 6)), ('hits', at(5, 9, 6))], quantities("Deal (5x5;{{Asc2|9|6x6}}) damage."))
        self.assertEqual([], quantities("Deals damage equal to its stored {{BD2|Steam Eruption}} amount, then dies."))

    def test_singular_cycles_and_bulleted_sequences_are_literal_sequences(self):
        from import_wiki_pages import claims_of, regions
        def sequences(body):
            text = "== Pattern ==\n" + body + "\n"
            return [(claim['kind'], claim['moves']) for claim in claims_of(text, 'Test', regions(text, 'Test')) if claim['kind'].startswith('fixed')]
        self.assertEqual([('fixed-cycle', ['Test.A', 'Test.B', 'Test.C'])], sequences("Follows a fixed cycle: {{Int2|Test|A}}, {{Int2|Test|B}}, {{Int2|Test|C}}, then repeats."))
        self.assertEqual([('fixed-cycle', ['Test.A', 'Test.B'])], sequences("Starts with {{Int2|Test|C}} and then repeats this cycle for the rest of combat:\n* {{Int2|Test|A}}\n* {{Int2|Test|B}}"))
        self.assertEqual([('fixed-path', ['Test.A', 'Test.B', 'Test.C'])], sequences("Follows a fixed sequence:\n* {{Int2|Test|A}} (stealing a card)\n* {{Int2|Test|B}}\n* {{Int2|Test|C}} (fleeing)"))
        self.assertEqual([], sequences("Follows a pattern:\n* {{Int2|Test|A}} or {{Int2|Test|B}}\n* {{Int2|Test|C}}"))

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
            self.assertNotIn('Other', ''.join(region['text'] for region in page['pattern_regions']))
            self.assertEqual(['Nibbit.Butt', 'Nibbit.Slice'], page['claims'][0]['moves'])
            receipt['pages'].pop()
            (root / 'sources.json').write_text(json.dumps(receipt))
            self.assertIn('Redirect target', import_pages(root)['pages'][0]['gaps'][0])

    def test_anchored_enemy_keeps_its_sibling_pattern_and_the_host_page_does_not(self):
        from import_wiki_pages import claims_of, regions
        text = ("== Pattern ==\n{{Intents Table/start|Host}}\n{{Intents Table/row\n|Name = Lash\n|Effect = Deals 4 damage.\n}}\n{{Intents Table/end}}\n"
                "== Notes ==\nHost notes.\n== Guest ==\n{{Enemy Infobox\n|Name = Guest\n}}\nIntroduction.\n"
                "== Pattern ==\nGuests alternate between {{Int2|Guest|Bite}} and {{Int2|Guest|Wriggle}}.\n== Trivia ==\nNot a pattern.\n")
        guest = regions(text, 'Guest', 'Guest')
        self.assertEqual(2, len(guest))
        self.assertTrue(text[guest[1][1]:].startswith('Guests alternate'))
        claims = claims_of(text, 'Guest', guest)
        self.assertEqual([('fixed-cycle', ['Guest.Bite', 'Guest.Wriggle'], 16)], [(claim['kind'], claim['moves'], claim['source_line']) for claim in claims])
        host = regions(text, 'Host')
        self.assertEqual(1, len(host))
        self.assertEqual(['Host.Lash'], [claim['moves'][0] for claim in claims_of(text, 'Host', host)])

    def test_every_phase_section_is_kept_with_its_table_owner_and_offset(self):
        from import_wiki_pages import claims_of, regions
        text = ("<tabber>\n|-|Phase 1=\n=== Pattern ===\n{{Intents Table/start|Boss}}\n{{Intents Table/row\n|Name = Bite\n|Effect = Deals 20 damage.\n}}\n"
                "|-|Phase 2=\n=== Pattern ===\n{{Intents Table/start|Boss (Phase 2)}}\n{{Intents Table/row\n|Name = Claw\n|Effect = Deals 10 damage.\n}}\n"
                "Uses {{Int2|Boss (Phase 2)|Claw}} every turn.\n</tabber>\n== Notes ==\nNot a pattern.\n")
        found = regions(text, 'Boss')
        self.assertEqual(2, len(found))
        claims = claims_of(text, 'Boss', found)
        self.assertEqual([('damage', 'Boss.Bite', 5), ('damage', 'Boss (Phase 2).Claw', 12), ('self-loop', 'Boss (Phase 2).Claw', 16)],
                         [(claim['kind'], claim['moves'][0], claim['source_line']) for claim in claims])
        for claim in claims:
            self.assertIn(text.splitlines()[claim['source_line'] - 1], claim['source_text'])

    def branch(self, line):
        from import_wiki_pages import claims_of, regions
        text = "== Pattern ==\n" + line + "\n"
        return [{field: claim.get(field) for field in ('kind', 'moves', 'weights', 'scope', 'repeat')} for claim in claims_of(text, 'Test', regions(text, 'Test'))
                if claim['kind'] in ('weights', 'repeat', 'unparsed')]

    def test_stated_odds_relative_weights_and_repeat_rules_are_literal_claims(self):
        self.assertEqual([{'kind': 'weights', 'moves': ['Test.A', 'Test.B'], 'weights': [2, 1], 'scope': 'opening', 'repeat': None}],
                         self.branch("On its first turn, it randomly picks {{Int2|Test|A}} (2/3 chance) or {{Int2|Test|B}} (1/3 chance)."))
        self.assertEqual([{'kind': 'weights', 'moves': ['Test.A', 'Test.B'], 'weights': [1, 2], 'scope': 'branch', 'repeat': None},
                          {'kind': 'repeat', 'moves': ['Test.A'], 'weights': None, 'scope': None, 'repeat': 'CannotRepeat'}],
                         self.branch("Always opens with {{Int2|Test|C}}. After that, randomly uses {{Int2|Test|A}} or {{Int2|Test|B}}, with {{Int2|Test|B}} being twice as likely. {{Int2|Test|A}} cannot be used twice in a row."))
        listed = self.branch("After any move, the next move is chosen randomly with equal probability:\n* 33% chance of {{Int2|Test|A}}\n* 33% chance of {{Int2|Test|B}}\n\nNo restriction on repeating the same move consecutively.")
        self.assertEqual([('weights', ['Test.A', 'Test.B'], [33, 33]), ('repeat', ['Test.A'], 'Unrestricted'), ('repeat', ['Test.B'], 'Unrestricted')],
                         [(claim['kind'], claim['moves'], claim['weights'] or claim['repeat']) for claim in listed])
        self.assertEqual([('repeat', 'CannotRepeat'), ('repeat', 'CannotRepeat'), ('repeat', 'CannotRepeat'), ('weights', [1, 1, 1])],
                         [(claim['kind'], claim['weights'] or claim['repeat']) for claim in self.branch(
                             "Randomly uses {{Int2|Test|A}}, {{Int2|Test|B}}, or {{Int2|Test|C}} each turn. No move can be used two turns in a row. All three moves are equally weighted.")])

    def test_odds_and_repetition_prose_that_binds_to_nothing_stays_unparsed(self):
        self.assertEqual(['unparsed'], [claim['kind'] for claim in self.branch(
            "Randomly picks {{Int2|Test|A}} (equal chance), {{Int2|Test|B}} (double chance), or {{Int2|Test|C}} (double chance).")])
        self.assertEqual(['unparsed'], [claim['kind'] for claim in self.branch("Randomly uses one of its three moves each turn. Cannot use the same move twice in a row.")])
        self.assertEqual(['repeat', 'unparsed'], [claim['kind'] for claim in self.branch(
            "It randomly picks either {{Int2|Test|A}} (cannot repeat) or {{Int2|Test|B}} (weight 2).")])
        self.assertEqual([], self.branch("Deals damage equal to its Strength, making you take 50% more damage."))

    def test_intent_rows_closing_inline_or_holding_templates_keep_their_own_boundaries(self):
        from import_wiki_pages import claims_of, regions
        text = ("== Pattern ==\n{{Intents Table/start|Test}}\n{{Intents Table/row\n|Name = Spit\n|Effect = Shuffles 10 {{C2|Slimed}} into the discard pile.}}\n"
                "{{Intents Table/row\n|Name = Pummel\n|Effect = Deals (4;{{Asc2|9|5}}) damage x4.}}\n"
                "{{Intents Table/row\n|Name = Hug\n|Effect = Applies 3 {{BD2|Weak}}.\n|GivesToPlayer = Weak\n}}\n"
                "{{Intents Table/row\n|Name = Smother\n|Effect = Deals 30 damage.}}\n{{Intents Table/end}}\nCycles {{Int2|Test|Spit}} then {{Int2|Test|Pummel}}.\n")
        claims = claims_of(text, 'Test', regions(text, 'Test'))
        self.assertEqual([('damage', 'Test.Pummel', 6, 4), ('hits', 'Test.Pummel', 6, 4), ('damage', 'Test.Smother', 14, 30), ('fixed-cycle', 'Test.Spit', 18, None)],
                         [(claim['kind'], claim['moves'][0], claim['source_line'], claim['amount'] and claim['amount']['base']) for claim in claims])
        for claim in claims:
            self.assertIn(claim['source_text'], text)
            self.assertNotIn('{{Intents Table/row', claim['source_text'][1:])

    def test_literal_use_limits_and_group_caps_bind_to_their_move(self):
        def limits(line):
            return [(claim['kind'], claim['moves'], claim['repeat'], claim.get('limit')) for claim in self.branch_full(line)]
        self.assertIn(('repeat', ['Test.Roar'], 'UseOnlyOnce', None), limits(
            "Randomly picks {{Int2|Test|Claw}} or {{Int2|Test|Roar}} with equal chance. It cannot use the same move twice in a row, and {{Int2|Test|Roar}} can only be used once per fight."))
        self.assertEqual([('repeat', ['Test.Enrage'], 'Cooldown', 3)], limits("* {{Int2|Test|Enrage}} can only be chosen once every 3 turns."))
        self.assertEqual([('repeat', ['Test.Call'], 'UseOnlyOnce', None), ('repeat', ['Test.Call'], 'GroupSummonCap', 3)], limits(
            "After at least two turns, a rat may use {{Int2|Test|Call}} to summon a new rat. Each rat can only summon once, and the group can summon up to three times total."))
        self.assertEqual([('repeat', ['Test.Call'], 'GroupSummonCap', 4)], limits(
            "Uses {{Int2|Test|Call}} to summon an ally. The group may summon at most 4 times total."))
        for ambiguous in (
            "The group can summon up to three times total.",
            "Uses {{Int2|Test|Call}} or {{Int2|Test|Backup}}. The group can summon up to three times total.",
            "Uses {{Int2|Test|Call}}. Each rat can summon up to three times total.",
            "Uses {{Int2|Test|Call}}. If healthy, the group can summon up to three times total.",
            "Uses {{Int2|Test|Call}}. The group can summon up to three times total or up to five times total.",
        ):
            with self.subTest(ambiguous=ambiguous):
                self.assertEqual([('unparsed', [], None, None)], limits(ambiguous))
        self.assertEqual([('unparsed', [], None, None)], limits("Each rat can only summon once."))

    def branch_full(self, line):
        from import_wiki_pages import claims_of, regions
        text = "== Pattern ==\n" + line + "\n"
        return [dict(claim, repeat=claim.get('repeat')) for claim in claims_of(text, 'Test', regions(text, 'Test')) if claim['kind'] in ('repeat', 'unparsed')]

    def test_an_opening_claim_needs_its_verb_and_move_in_one_sentence(self):
        from import_wiki_pages import claims_of, regions
        def openings(line):
            text = "== Pattern ==\n" + line + "\n"
            return [claim['moves'] for claim in claims_of(text, 'Egg', regions(text, 'Egg')) if claim['kind'] == 'opening-alternative']
        self.assertEqual([], openings("The Egg is summoned via {{Int2|Mother|Lay Eggs}}. It starts with the {{BD2|Hatch}} power."))
        self.assertEqual([['Egg.Crack']], openings("The Egg is summoned by its mother. It always opens with {{Int2|Egg|Crack}}."))
        self.assertEqual([['Egg.Crack']], openings("Uses {{Int2|Egg|Crack}} on its first turn, then waits."))

    def test_cached_pages_state_the_odds_and_repeat_rules_the_cross_check_needs(self):
        root = pathlib.Path(__file__).resolve().parents[2] / "src" / "EncounterIntel" / "catalogue" / "wiki-page-sources"
        pages = {page["name"]: page for page in import_pages(root)["pages"]}
        def stated(name, kind):
            return [(claim["moves"], claim.get("weights") or claim.get("repeat"), claim.get("scope")) for claim in pages[name]["claims"] if claim["kind"] == kind]
        self.assertIn((["Flyconid.Frail Spores", "Flyconid.Smash"], [2, 1], "opening"), stated("Flyconid", "weights"))
        self.assertEqual([(["Hunter Killer.Bite", "Hunter Killer.Puncture"], [1, 2], "branch")], stated("Hunter Killer", "weights"))
        self.assertEqual([([move], "Unrestricted", None) for move in ("Fossil Stalker.Latch", "Fossil Stalker.Tackle", "Fossil Stalker.Lash")],
                         stated("Fossil Stalker", "repeat"))
        self.assertEqual([52, 64], [region["source_line"] for region in pages["Wriggler"]["pattern_regions"]])
        self.assertIn((["Wriggler.Nasty Bite", "Wriggler.Wriggle"], None, None), stated("Wriggler", "fixed-cycle"))
        self.assertEqual([35, 84, 120], [region["source_line"] for region in pages["Test Subject"]["pattern_regions"]])
        self.assertIn((["Test Subject (Phase 2).Multi-Claw"], None, None), stated("Test Subject", "damage"))
        self.assertIn((["Mawler.Roar"], "UseOnlyOnce", None), stated("Mawler", "repeat"))
        self.assertIn((["The Merchant???.Enrage"], "Cooldown", None), stated("The Merchant???", "repeat"))
        self.assertIn((["Two-Tailed Rat.Call for Backup"], "UseOnlyOnce", None), stated("Two-Tailed Rat", "repeat"))
        self.assertIn((["Two-Tailed Rat.Call for Backup"], "GroupSummonCap", None), stated("Two-Tailed Rat", "repeat"))
        rat_cap = next(claim for claim in pages["Two-Tailed Rat"]["claims"] if claim.get("repeat") == "GroupSummonCap")
        self.assertEqual(3, rat_cap["limit"])
        self.assertEqual(37, rat_cap["source_line"])
        self.assertEqual([], stated("Tough Egg", "opening-alternative"))
        for page, moves in (("Globe Head", ["Globe Head.Shocking Slap", "Globe Head.Thunder Strike", "Globe Head.Galvanic Burst"]),
                            ("Entomancer", ["Entomancer.Beeeees!", "Entomancer.Spear!", "Entomancer.Pheromone Spit"]),
                            ("Louse Progenitor", ["Louse Progenitor.Web Cannon", "Louse Progenitor.Curl and Grow", "Louse Progenitor.Pounce"]),
                            ("Mecha Knight", ["Mecha Knight.Flamethrower", "Mecha Knight.Windup", "Mecha Knight.Heavy Cleave"])):
            self.assertIn((moves, None, None), stated(page, "fixed-cycle"))
        self.assertEqual(4, len(stated("Owl Magistrate", "fixed-cycle")[0][0]))
        self.assertEqual(5, len(stated("Thieving Hopper", "fixed-path")[0][0]))
        self.assertIn((["Slimed Berserker.Furious Pummeling"], None, None), stated("Slimed Berserker", "hits"))
        self.assertIn((["Infested Prism.Pulsate"], None, None), stated("Infested Prism", "damage"))
        self.assertEqual(["Slimed Berserker.Furious Pummeling", "Slimed Berserker.Smother"], [moves[0] for moves, _, _ in stated("Slimed Berserker", "damage")][-2:])
        for page in pages.values():
            lines = page["source_text"].splitlines()
            for region in page["pattern_regions"]:
                self.assertTrue(lines[region["source_line"] - 1].strip() and region["text"].startswith(lines[region["source_line"] - 1].strip()))
            for claim in page["claims"]:
                self.assertIn(claim["source_text"], page["source_text"])
                self.assertIn(lines[claim["source_line"] - 1], claim["source_text"])

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
