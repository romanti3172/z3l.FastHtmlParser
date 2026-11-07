# z3l.FastHtmlParser

Fast, low-allocation HTML / XML parser for scraping and data extraction on .NET 9 and .NET 10.

- **Fast parsing.** 9.6–36x faster than AngleSharp and HtmlAgilityPack with 13–23x less memory (see the benchmarks); a 50 KB page parses in about 55 μs
  with a single exact-size list of tags.
- **No allocations when you match tags or attributes.** Use `HtmlTag` / `HtmlAttribute` enums
  (about 250 values each: HTML, SVG, MathML, RSS, Atom, sitemap) or plain strings.
- **HTML5 entities.** All 2125 named references, legacy names without `;`, numeric references —
  decoded the way browsers do.
- **Text extraction.** `Text()` gives clean text; `TextLineBreaks()` keeps the visible line
  structure (`<br>`, `<p>`, `<li>`, `<div>`...).
- **Null-safe chains.** Every method accepts `null` and returns `null` / empty, so a chain never
  throws when something is missing.
- **Thread-safe reads.** A parsed document is read-only; query it from any number of threads.

## Install

```
dotnet add package z3l.FastHtmlParser
```

## Quick start

```csharp
using z3l.FastHtmlParser;

var doc = new HtmlParser(html, "https://example.com/catalog/");

// first match
var title = doc.Find(HtmlTag.H1).Text();
var price = doc.Find(HtmlTag.Span, cls: "price").Text();

// chains never throw: a missing tag gives null at the end
var phone = doc.Find(HtmlTag.Div, id: "contacts")
               .Find(HtmlTag.A, cls: "phone")
               .AttributeHrefTelephone();

// all matches; links are made absolute against the page URL
foreach (var a in doc.Find(HtmlTag.Div, cls: "gallery").FindAll(HtmlTag.A))
{
    Console.WriteLine(a.AttributeHref());
}
```

## Finding tags

`Find` returns the first match, `FindAll` all matches (lazily), `Is` checks one tag. They work on the
whole document or inside a tag (`tag.Find(...)` searches its descendants).

```csharp
doc.Find(HtmlTag.Table);
doc.Find(HtmlTag.Li, cls: "item");                        // one of the space-separated classes
doc.Find(HtmlTag.Div, id: "main");
doc.Find(HtmlTag.Dt, text: "Price");                      // visible text: tags removed, entities decoded
doc.Find("custom-element");                               // names that are not in HtmlTag

// any combination of criteria
doc.Find(new TagQuery(HtmlTag.Meta) { Attribute = HtmlAttribute.Property, AttributeValue = "og:title" });
doc.FindAll(new TagQuery(HtmlTag.Img) { AttributeName = "data-lazy", AttributeValue = "true" });
doc.Find(new TagQuery { IdStartsWith = "product-" });     // any tag
tag.Is(new TagQuery(HtmlTag.Li) { Class = "item", TextStartsWith = "Price" });
```

`TagQuery` criteria: `Class`, `Id`, `IdStartsWith`, `Attribute` / `AttributeName` + `AttributeValue`,
`Text`, `TextStartsWith`, `Body`, `BodyStartsWith`, `Source`. Null criteria are not checked.
A query is a struct: searching allocates nothing, and the name is compared first with one integer comparison.

## Shortcuts: a method for every search

```csharp
using z3l.FastHtmlParser.Shortcuts;

doc.FindByNameClass(HtmlTag.Span, "price");
doc.FindByNameText(HtmlTag.Dt, "Price").NextSiblingByName(HtmlTag.Dd).Text();
doc.FindAllByNameAttribute(HtmlTag.Img, HtmlAttribute.DataSrc, "...");
doc.FindByNameItemprop(HtmlTag.Span, "price");
tag.IsNameClass(HtmlTag.Div, "card");
tag.ParentByName(HtmlTag.Table);
tag.AttributeTitle();
```

Every shortcut is a one-line call of the core API above; they live in their own namespace,
so the core API stays small unless you import them.

## Reading a tag

```csharp
tag.Name;              // "div"
tag.Text();            // visible text, entities decoded
tag.TextLineBreaks();  // text with line breaks: <br>, block elements → new line, <p> → paragraph
tag.Body();            // inner HTML
tag.FullSource();      // outer HTML
tag.AttributeHref();   // absolute URL
tag.AttributeByName(HtmlAttribute.Content);   // decoded value
tag.AttributeBody(HtmlAttribute.Content);     // value as written in the source
```

## Navigation

```csharp
tag.Parent();
tag.Parent(new TagQuery(HtmlTag.Table));              // nearest enclosing <table>
tag.Children();                                       // direct children
foreach (var descendant in tag) { }                   // all tags inside
tag.NextSibling();
tag.NextSibling(new TagQuery(HtmlTag.Dd));            // <dt>Price</dt><dd>100</dd>
tag.PreviousSibling();
tag.Next(new TagQuery(HtmlTag.Div));                  // first <div> after this tag
```

Example: a definition list.

```csharp
var price = doc.Find(HtmlTag.Dt, text: "Price").NextSibling(new TagQuery(HtmlTag.Dd)).Text();
```

## Text helpers for raw HTML

```csharp
HtmlText.Decode("Q&amp;A &copy; 2026");            // "Q&A © 2026"
HtmlText.ToText("<b>Hello</b>   world");           // "Hello world"
HtmlText.ToTextWithLineBreaks("<p>One</p><p>Two</p>");
```

## Parsing rules

The parser builds a flat list of tags, not a full DOM, and follows the HTML rules that matter for scraping:

- `<!-- comments -->` are single `HtmlTag.Comment` tags; markup inside them is ignored.
- The content of `script`, `style`, `textarea`, `title`, `iframe`, `xmp`, `noembed`, `noframes` is text, not markup.
- Void elements (`img`, `br`, `meta`, `link`, `input`, `hr`, ...) and `<!doctype>` never contain other tags.
- `Text()`, `IsText` and `TextLineBreaks()` return the visible text: comments and the content of `script`, `style`,
  `noscript`, `template`, `noframes`, `noembed` are skipped (`Text()` of a `<script>` itself still gives its code).
- Unclosed tags are closed by the end tag of an enclosing element (`<li>one<li>two</ul>`).
- Quoted attribute values may contain `<` and `>`.
- A document that starts with `<?xml` (RSS, Atom, sitemap, XHTML) is parsed by XML rules:
  an element is closed only by its end tag or `/>`, so an RSS `<link>` keeps its content.
- Attribute values are returned decoded, as a browser reads them (`title="It&#39;s"` → `It's`), and `Is*` / `Find*`
  compare decoded values. `AttributeBody` returns the value exactly as written. Links (`AttributeHref`, `AttributeSrc`)
  are not decoded, only resolved against the page URL with `&amp;`, `%3f`, `%3d` unescaped.

On the test pages the parser finds the same elements, texts, attribute values and parents as AngleSharp,
apart from the trees a browser builds implicitly (an inserted `<tbody>`, an ignored second `<body>`).

## Tests and benchmarks

```
dotnet test
dotnet run -c Release --project benchmarks/z3l.FastHtmlParser.Benchmarks -- --filter '*'
```

The tests run on saved pages of quotes.toscrape.com, books.toscrape.com and webscraper.io/test-sites
and compare every page with AngleSharp. The benchmarks compare parsing and a complete scraping task
with AngleSharp and HtmlAgilityPack; see [benchmarks/README.md](benchmarks/README.md).

| books.toscrape.com, 51 KB: parse + extract 20 books | Mean | Allocated |
|---|---:|---:|
| **z3l.FastHtmlParser** | **65 μs** | **47 KB** |
| HtmlAgilityPack | 799 μs (12x) | 1,038 KB (22x) |
| AngleSharp | 1,016 μs (16x) | 707 KB (15x) |

## License

Free for individuals and organizations with annual revenue **below USD 1,000,000** and
**fewer than 100** employees, including commercial use. Organizations with annual revenue of
USD 1,000,000 or more, or with 100 or more employees, need a commercial license: a **one-time payment of USD 1,000** per organization, perpetual, all versions.
See [LICENSE.md](LICENSE.md) for the full terms.
