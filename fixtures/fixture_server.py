"""Local fixture server for exercising the recipe importer.

Serves pages covering the JSON-LD shapes that appear in the wild, plus the
failure and SSRF cases the importer has to reject. Development scratch tool;
not part of the API.

    python fixtures/fixture_server.py [port]
"""
import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def page(ld, body="<h1>Fixture</h1>"):
    blocks = ld if isinstance(ld, list) else [ld]
    scripts = "\n".join(
        f'<script type="application/ld+json">{b if isinstance(b, str) else json.dumps(b)}</script>'
        for b in blocks
    )
    return f"<!doctype html><html><head><title>Fixture</title>{scripts}</head><body>{body}</body></html>"


SIMPLE = {
    "@context": "https://schema.org",
    "@type": "Recipe",
    "name": "Fixture Lemon Chicken",
    "description": "A bright, fast weeknight chicken.",
    "image": "https://example.com/lemon-chicken.jpg",
    "prepTime": "PT15M",
    "cookTime": "PT35M",
    "recipeYield": "4 servings",
    "recipeCuisine": "Mediterranean",
    "keywords": "quick, chicken, weeknight",
    "recipeIngredient": [
        "4 boneless, skinless chicken thighs",
        "2 lemons, zested and juiced",
        "3 cloves garlic, minced",
        "2 tbsp olive oil",
        "1 tsp dried oregano",
    ],
    "recipeInstructions": [
        {"@type": "HowToStep", "text": "Marinate the chicken in lemon, garlic and oregano."},
        {"@type": "HowToStep", "text": "Sear skin-side down until deeply browned."},
        {"@type": "HowToStep", "text": "Finish in the oven at 200C for 20 minutes."},
    ],
}

# Recipe nested inside @graph alongside a WebPage, with @type as an array.
GRAPH = {
    "@context": "https://schema.org",
    "@graph": [
        {"@type": "WebSite", "name": "Fixture Food"},
        {"@type": "WebPage", "name": "A page about a recipe"},
        {
            "@type": ["Recipe", "NewsArticle"],
            "name": "Fixture Graph Curry",
            "description": "Nested under @graph, typed as an array.",
            "image": {"@type": "ImageObject", "url": "https://example.com/curry.jpg"},
            "totalTime": "PT1H10M",
            "recipeYield": ["6", "6 servings"],
            "recipeCuisine": ["Indian", "British"],
            "keywords": ["curry", "batch-cooking"],
            "recipeIngredient": [
                "500g chicken breast, diced",
                "1 tin (400g) chopped tomatoes",
                "2 onions, finely sliced",
                "2 tbsp garam masala",
            ],
            "recipeInstructions": [
                "Brown the onions slowly.",
                "Add spices, then tomatoes.",
                "Simmer the chicken through.",
            ],
        },
    ],
}

# Instructions as HowToSection wrapping nested HowToStep lists.
SECTIONS = {
    "@context": "https://schema.org",
    "@type": "Recipe",
    "name": "Fixture Sectioned Cake",
    "prepTime": "PT30M",
    "cookTime": "PT45M",
    "recipeYield": 8,
    "recipeIngredient": ["200g plain flour", "200g caster sugar", "4 eggs", "200g butter, softened"],
    "recipeInstructions": [
        {
            "@type": "HowToSection",
            "name": "Make the batter",
            "itemListElement": [
                {"@type": "HowToStep", "text": "Cream the butter and sugar."},
                {"@type": "HowToStep", "text": "Beat in the eggs one at a time."},
            ],
        },
        {
            "@type": "HowToSection",
            "name": "Bake",
            "itemListElement": [
                {"@type": "HowToStep", "text": "Fold in the flour."},
                {"@type": "HowToStep", "text": "Bake at 180C for 45 minutes."},
            ],
        },
    ],
}

# Instructions as one HTML blob, and a loose (non-ISO) time string.
BLOB = {
    "@context": "https://schema.org",
    "@type": "Recipe",
    "name": "Fixture Blob Soup",
    "totalTime": "45 minutes",
    "recipeYield": "Serves 4",
    "recipeIngredient": ["1 kg carrots, peeled", "1 onion", "1 litre vegetable stock"],
    "recipeInstructions": "<ol><li>Sweat the onion.</li><li>Add carrots and stock.</li><li>Blend until smooth.</li></ol>",
}

ROUTES = {
    "/simple": page(SIMPLE),
    "/graph": page(GRAPH),
    "/sections": page(SECTIONS),
    "/blob": page(BLOB),
    # A malformed block first: the extractor must skip it and find the next.
    "/malformed-then-good": page(["{ this is not json ]", SIMPLE]),
    "/no-recipe": page({"@context": "https://schema.org", "@type": "WebPage", "name": "Just a page"}),
    "/no-jsonld": "<!doctype html><html><body><h1>Nothing structured here</h1></body></html>",
    # Usable-check: has a name but no ingredients or steps.
    "/incomplete": page({"@context": "https://schema.org", "@type": "Recipe", "name": "Nameless Only"}),
    # Reachable and perfectly valid, but robots.txt puts it off limits.
    "/blocked/secret-recipe": page(SIMPLE),
}

# A wildcard group the crawler must obey, plus a named group that takes
# precedence over it, plus an Allow carving an exception out of a Disallow.
ROBOTS = """\
User-agent: *
Disallow: /

User-agent: RecipeFinderBot
Crawl-delay: 0
Disallow: /blocked/
Allow: /
"""


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def _send(self, code, body=b"", ctype="text/html; charset=utf-8", extra=None):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if body:
            self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?")[0]

        if path == "/robots.txt":
            return self._send(200, ROBOTS.encode(), "text/plain; charset=utf-8")

        if path in ROUTES:
            return self._send(200, ROUTES[path].encode())

        if path == "/redirect":
            return self._send(302, b"", extra={"Location": "/simple"})

        if path == "/redirect-chain":
            return self._send(302, b"", extra={"Location": "/redirect"})

        if path == "/redirect-to-metadata":
            # The SSRF case: a public-looking URL bouncing to cloud metadata.
            return self._send(302, b"", extra={"Location": "http://169.254.169.254/latest/meta-data/"})

        if path == "/redirect-loop":
            return self._send(302, b"", extra={"Location": "/redirect-loop"})

        if path == "/not-html":
            return self._send(200, json.dumps({"hello": "world"}).encode(), "application/json")

        if path == "/huge":
            filler = "<p>padding</p>" * 250_000  # ~3.5MB, over the 2MB ceiling
            return self._send(200, page(SIMPLE, filler).encode())

        if path == "/boom":
            return self._send(500, b"<h1>Server error</h1>")

        self._send(404, b"<h1>Not found</h1>")


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8099
    print(f"fixture server on http://127.0.0.1:{port}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
