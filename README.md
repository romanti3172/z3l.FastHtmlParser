# z3l.FastHtmlParser

Fast, low-allocation HTML / XML parser for scraping and data extraction on .NET 9 and .NET 10.

**Website:** https://romanti3172.github.io/z3l.FastHtmlParser/ · **NuGet:** https://www.nuget.org/packages/z3l.FastHtmlParser

- **Fast parsing.** About 10x faster than HtmlAgilityPack and AngleSharp on typical pages,
  up to 30x on large ones, with 12–23x less memory (see the benchmarks); a 50 KB page parses in
  about 60 μs with a single exact-size list of tags.
- **No allocations when you match tags or attributes.** Use `HtmlTag` / `HtmlAttribute` enums
  (about 250 values each: HTML, SVG, MathML, RSS, Atom, sitemap) or plain strings.
- **HTML5 entities.** All 2125 named references, legacy names without `;`, numeric references —
  decoded the way browsers do.
- **Text extraction.** `Text()` gives clean text; `TextLineBreaks()` keeps the visible line
  structure (`<br>`, `<p>`, `<li>`, `<div>`...).
- **XPath 1.0.** `doc.XPath("//div[@class='card']/a")`, `doc.XPathValues("//a/@href")`, compiled and cached.
- **CSS selectors.** `doc.Css("div.card > a[href^='/item']")`, `:nth-child()`, `:not()`, `:has()`, compiled and cached.
- **Parse from buffers.** `HtmlParser.Parse(memory)` reads a `char[]` or a pooled buffer in place, without a copy.
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
tag.Parent(HtmlTag.Table);                            // nearest enclosing <table>
tag.Parent(new TagQuery(HtmlTag.Div) { Class = "card" });
tag.Children();                                       // direct children
foreach (var descendant in tag) { }                   // all tags inside
tag.NextSibling();
tag.NextSibling(HtmlTag.Dd);                          // <dt>Price</dt><dd>100</dd>
tag.PreviousSibling();
tag.Next(HtmlTag.Div);                                // first <div> after this tag
```

Wherever a `TagQuery` is expected, a plain `HtmlTag` or a tag name works too: it means "a tag with this name"
(`tag.NextSibling(HtmlTag.Dd)`, `tag.NextSibling("dd")`, `tag.Parent("my-widget")`).

Example: a definition list.

```csharp
var price = doc.Find(HtmlTag.Dt, text: "Price").NextSibling(HtmlTag.Dd).Text();
```

## XPath

XPath 1.0 works on the document or inside a tag. `XPath` returns tags, `XPathValues` / `XPathValue` return
values: decoded attributes, `text()`, the visible text of tags, or the result of `count()`, `sum()`, `string()`...

```csharp
doc.XPath("//div[@class='card']//span[@class='price']");
doc.XPathFirst("//ul[@id='menu']/li[last()]/a");
doc.XPathValues("//a[contains(@href, '/product/')]/@href");
doc.XPathValue("//dt[.='Price']/following-sibling::dd[1]");
doc.XPathValue("count(//table//tr)");                       // "12"

var card = doc.XPathFirst("//div[@id='main']");
card.XPath(".//a");                                         // relative paths start at the tag
card.XPath("//a");                                          // '/' and '//' search the whole document

// compile once, reuse on any number of pages and threads
static readonly XPathQuery Titles = XPathQuery.Compile("//article//h3/a/@title");
page.XPathValues(Titles);
```

All axes (except namespace), predicates, `|`, operators and the XPath 1.0 functions are supported,
plus `ends-with()`, `lower-case()` and `upper-case()`. HTML rules apply: names are case-insensitive, entities
are decoded, and the string-value of an element is its `Text()` (whitespace collapsed, script/style dropped).
`//name` and `[@attr='value']` run as a linear scan of the tag list with in-place attribute comparison.
String expressions are compiled once and cached; an invalid expression throws `ArgumentException`
with the position of the error.

## CSS selectors

`Css`, `CssFirst` and `IsCss` work as `querySelectorAll`, `querySelector` and `matches` in a browser.

```csharp
doc.Css("article.product_pod h3 > a");
doc.CssFirst("ul#menu > li:last-child a")?.AttributeHref();
doc.Css("table tr:nth-child(odd) > td:nth-child(2)");
doc.Css("div.quote:has(a.tag[href*=love]) span.text");
doc.Css("a[href$='.pdf' i]:not([rel~=nofollow])");
doc.Css("h2:contains('Price') + p");                        // the visible text contains the string

var list = doc.CssFirst("ul.menu");
list.Css("> li");                                           // its children
list.Css(":scope > li > a");
link.IsCss("nav a.active");

// compile once, reuse on any number of pages and threads
static readonly CssQuery Prices = CssQuery.Compile("div.card span.price");
page.Css(Prices);
```

Supported: type, `*`, `#id`, `.class`, attribute selectors (`[a]`, `=`, `~=`, `|=`, `^=`, `$=`, `*=`, with the `i` / `s`
flags), the four combinators, selector lists, `:first-child`, `:last-child`, `:only-child`, the `-of-type` variants,
`:nth-child(An+B [of S])`, `:nth-last-child()`, `:nth-of-type()`, `:nth-last-of-type()`, `:not()`, `:is()`, `:where()`,
`:has()`, `:empty`, `:root`, `:scope`, `:link`, `:checked`, `:disabled`, `:enabled`, `:required`, `:optional`, and the
non-standard `:contains()`. Ids, classes and attribute values are case-sensitive as in a standards-mode page, except
the values HTML compares ignoring case (`type`, `rel`, `lang`, ...). Results are cross-checked against AngleSharp.
A simple selector runs as fast as `FindAll`; `tag.Css("div a")` matches the whole selector against the document,
as in a browser, and returns the tags inside `tag`.

## Parsing from a buffer

Text that is already in a `char[]` or a pooled buffer can be parsed in place, without copying it into a string.

```csharp
var doc = HtmlParser.Parse(chars.AsMemory(0, length), url);   // the buffer must not change while doc is used

// hand a pooled buffer over: Dispose returns it to the pool
var owner = MemoryPool<char>.Shared.Rent(maxChars);
int length = reader.Read(owner.Memory.Span);
using var page = HtmlParser.Parse(owner, length, url);
```

Everything works as with a string; `PageSource` makes the string on its first use.

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
- Optional end tags work as in HTML5: a new `<li>` ends an open `<li>`, `<td>`/`<tr>` end open cells and rows,
  `<dt>`/`<dd>` and `<option>` end their open siblings, a block element (`<div>`, `<ul>`, `<table>`, ...) ends an open `<p>`.
- Tags left open are ended by the end tag of an enclosing element (`<li>one<li>two</ul>`) or by the end of the document,
  so a truncated page keeps its text.
- `<` starts a tag only before a letter, `/`, `!` or `?`: in `a < b` it is text. Stray end tags (`x</span>`) are not text.
- Quoted attribute values may contain `<` and `>`, and text that looks like another attribute:
  in `<a title="see id=5" id="real">` the id is `real`.
- A document that starts with `<?xml` (RSS, Atom, sitemap, XHTML) is parsed by XML rules:
  an element is closed only by its end tag or `/>`, so an RSS `<link>` keeps its content.
- Attribute values are returned decoded, as a browser reads them (`title="It&#39;s"` → `It's`), and `Is*` / `Find*`
  compare decoded values. `AttributeBody` returns the value exactly as written. Links (`AttributeHref`, `AttributeSrc`)
  are decoded the same way (`&amp;` → `&`) and resolved against the page URL; percent-escapes are kept as written
  (`%3F` in a path is not a `?`).
- Nesting deeper than 512 levels is flattened, as in browsers: a deeper tag is found and has its text,
  but it does not become the parent of the tags inside it.
- Parsing time grows linearly with the size of the page on any markup, including thousands of unclosed tags
  and stray end tags.

A document does not have to be disposed: it holds no unmanaged resources. `Dispose()` releases the source text
and the tag list early (and returns a buffer handed over with `HtmlParser.Parse(owner, length)` to its pool);
after it the document and its tags throw `ObjectDisposedException`.

On the test pages and on a set of broken-markup snippets (left-out end tags, stray end tags, truncated pages)
the parser finds the same elements, texts, attribute values and parents as AngleSharp, apart from the trees
a browser builds implicitly (an inserted `<tbody>`, an ignored second `<body>`).

## Tests and benchmarks

```
dotnet test
dotnet run -c Release --project benchmarks/z3l.FastHtmlParser.Benchmarks -- --filter '*'
```

The tests run on saved pages of quotes.toscrape.com, books.toscrape.com and webscraper.io/test-sites
and compare every page with AngleSharp, CSS selector results included. The benchmarks compare parsing and a complete scraping task
with AngleSharp and HtmlAgilityPack; see [benchmarks/README.md](benchmarks/README.md).

| books.toscrape.com, 51 KB: parse + extract 20 books | Mean | Allocated |
|---|---:|---:|
| **z3l.FastHtmlParser** | **71 μs** | **45 KB** |
| HtmlAgilityPack | 862 μs (12x) | 1,038 KB (23x) |
| AngleSharp | 1,160 μs (16x) | 707 KB (16x) |

## License

Free for individuals and organizations with annual revenue **below USD 1,000,000** and
**fewer than 100** employees, including commercial use. Organizations with annual revenue of
USD 1,000,000 or more, or with 100 or more employees, need a commercial license: a **one-time payment of USD 1,000** per organization, perpetual, all versions.
See [LICENSE.md](LICENSE.md) for the full terms and [pricing](https://romanti3172.github.io/z3l.FastHtmlParser/#pricing) to buy a commercial license.
