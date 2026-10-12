"""Import a cached, authorized enemy-page bundle offline; never fetch or evaluate it."""
import argparse
import hashlib
import json
import pathlib
import re
import urllib.parse


def import_pages(directory):
    root = pathlib.Path(directory).resolve()
    receipt = json.loads((root / "sources.json").read_text())
    if set(receipt) != {"schema", "acquisition", "pages"} or receipt["schema"] != 1:
        raise ValueError("Unknown page receipt schema")
    if not isinstance(receipt["acquisition"], str) or not receipt["acquisition"]:
        raise ValueError("Missing acquisition disposition")
    pages = []
    names = set()
    for page in receipt["pages"]:
        if page["name"] in names:
            raise ValueError("Duplicate enemy page")
        names.add(page["name"])
        revision = page["revision"]
        if not isinstance(revision, int) or isinstance(revision, bool) or revision <= 0:
            raise ValueError("Missing source revision")
        url = urllib.parse.urlsplit(page["source_url"])
        expected_path = "/wiki/" + urllib.parse.quote("Slay_the_Spire_2:" + page["name"].replace(" ", "_"), safe=":")
        if url.scheme != "https" or url.netloc != "slaythespire.wiki.gg" or url.path != expected_path or url.fragment:
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
        text = raw.decode("utf-8")
        sections = re.findall(r"(?im)^={2,6}\s*(?:move\s+pattern|behavior|behaviour|pattern)\s*={2,6}\s*\n([\s\S]*?)(?=^={2,6}[^=]|\Z)", text)
        pages.append({"name": page["name"], "source": {"module": "enemy-page:" + page["name"], "url": page["source_url"], "revision": revision,
                      "fetched_at": page["fetched_at"], "sha256": page["sha256"]}, "source_text": text,
                      "pattern_text": "\n".join(sections).strip() or None})
    return {"schema": 1, "acquisition": receipt["acquisition"], "pages": pages}


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
