import argparse
import html
import json
import shutil
from html.parser import HTMLParser
from pathlib import Path

PAGES = [
    ("index.html", "Sledders Lua", "Home"),
    ("install.html", "Install the runtime", "Install runtime"),
    ("mods.html", "Install Lua mods", "Install Lua mods"),
    ("first-mod.html", "Make your first mod", "First mod"),
    ("lua.html", "Lua basics", "Lua basics"),
    ("callbacks.html", "Callbacks", "Callbacks"),
    ("sled.html", "Sled", "Sled"),
    ("input.html", "Input", "Input"),
    ("drawing.html", "Drawing", "Drawing"),
    ("storage.html", "Storage", "Storage"),
    ("folder-mods.html", "Folder mods", "Folder mods"),
    ("api.html", "API reference", "API reference"),
    ("examples.html", "Examples", "Examples"),
    ("troubleshooting.html", "Troubleshooting", "Troubleshooting"),
    ("releases.html", "Releases", "Releases"),
]


def build_page(title, current, body):
    nav = "\n".join(
        f'          <a href="{href}"{" class=\"active\"" if href == current else ""}>{html.escape(label)}</a>'
        for href, _, label in PAGES
    )
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{html.escape(title)} - Sledders Lua</title>
  <link rel="stylesheet" href="assets/style.css">
  <script src="assets/config.js"></script>
  <script defer src="assets/site.js"></script>
</head>
<body>
  <div class="layout">
    <aside class="sidebar">
      <div class="brand"><a href="index.html">Sledders Lua</a></div>
      <input class="search" type="search" placeholder="Filter pages" aria-label="Filter pages">
      <nav class="nav">
{nav}
      </nav>
    </aside>
    <main>
{body.rstrip()}
      <footer>Sledders Lua Runtime · MIT licensed</footer>
    </main>
  </div>
</body>
</html>
"""


class LinkCollector(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if key in {"href", "src"} and value:
                self.links.append(value)


def validate_links(output):
    errors = []
    for page in output.rglob("*.html"):
        parser = LinkCollector()
        parser.feed(page.read_text(encoding="utf-8"))
        for link in parser.links:
            if link.startswith(("http://", "https://", "mailto:", "#", "javascript:")):
                continue
            clean = link.split("#", 1)[0].split("?", 1)[0]
            if not clean:
                continue
            target = (page.parent / clean).resolve()
            if clean.endswith("/") or target.is_dir():
                target = target / "index.html"
            if not target.exists():
                errors.append(f"{page.relative_to(output)} -> {link}")
    if errors:
        raise SystemExit("Broken site links:\n" + "\n".join(errors))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    parser.add_argument("--output", required=True)
    parser.add_argument("--repository", default="")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    output = Path(args.output).resolve()
    site = root / "site"

    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)

    shutil.copytree(site / "assets", output / "assets")
    shutil.copytree(root / "examples", output / "examples-src")
    shutil.copy2(root / "docs/API.api", output / "api.api")
    (output / ".nojekyll").write_text("", encoding="utf-8")
    (output / "assets/config.js").write_text(
        "window.SLEDDERS_LUA_REPOSITORY = " + json.dumps(args.repository.strip()) + ";\n",
        encoding="utf-8",
    )

    api_text = html.escape((root / "docs/API.api").read_text(encoding="utf-8"))
    for filename, title, _ in PAGES:
        fragment = (site / "pages" / filename).read_text(encoding="utf-8")
        fragment = fragment.replace("{{API}}", api_text)
        (output / filename).write_text(build_page(title, filename, fragment), encoding="utf-8")

    validate_links(output)
    print(f"Built {len(PAGES)} pages in {output}")


if __name__ == "__main__":
    main()
