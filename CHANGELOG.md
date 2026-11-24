# Changelog

## 1.8.2 (2026-11-24)

### Changed

- README (the package page on NuGet): "Other languages" lists all 10 languages and 11 packages, and says which
  features (HTML to Markdown, `Article()`, `Sanitize()`, forms) are in .NET only. No changes to the code.

## 1.8.1 (2026-11-22)

### Fixed

- `Sanitize()` took quadratic time on deeply nested markup: before each block it looked through all the open elements
  for a `<p>` to end (6 s for 100,000 nested `<div>`, 1 MB; 101 s for 4 MB). It now looks only when such an element is
  open, and at most 1024 elements deep: 8 ms and 26 ms.
- `Article()` read the text of every nested heading, byline and small block of the page (12 s for 400,000 nested
  `<div>`); it reads only those with at most 4 KB of markup. `IsProbablyReadable()` stops after reading four times
  the page.
- `ReadableArticle.Image` was "…/image/jpeg" on pages with `og:image:type` after `og:image`: the meta property
  `og:image` was found at the start of `og:image:type`. A property name must now end where the pattern ends.
- `ReadableArticle.Image` is only an http or https address (or a relative one on a page without a URL), never a
  `javascript:` or `data:` one from the page's metadata.

## 1.8.0 (2026-11-22)

### Added

- `Article()`: the main content of a page by the method of Mozilla's Readability (the reader view of Firefox), as
  `ReadableArticle`: the article without menus, sidebars, comments, ads and footers as clean HTML (`Content`),
  `Markdown` and `TextContent`, with `Title`, `Byline`, `Excerpt`, `SiteName`, `PublishedTime`, `Image` and
  `Language` from JSON-LD, Open Graph, Twitter and Dublin Core metadata or from the page. The page is not changed:
  what Readability removes is marked in the tag list, the text is counted in one pass. On the 130 test pages of
  Mozilla Readability the text of the article matches the expected one at 0.99 (mean F1 by words) and 498 of 502
  metadata fields are the same. `IsProbablyReadable()` is Readability's quick test for pages with an article.
- `Sanitize()` and `HtmlText.Sanitize()`: untrusted HTML made safe to put in a page, with `SanitizeOptions` (allowed
  tags, attributes, URL schemes, CSS properties, tags removed with their content, `data-*` attributes, inlined images,
  absolute URLs, `rel` of links). The result is written anew: kept tags by their lower-case names with their kept
  attributes in double quotes, every text and value escaped, every element closed, and the end tags a browser would
  imply written where it would imply them. Event handlers, `javascript:` and other schemes that are not allowed,
  CSS with `url()` or `expression()`, `svg`, `math`, `style`, `template` and the other tags whose content a browser
  does not read as HTML are never written. Checked against AngleSharp on random broken markup: a browser sees only
  allowed elements and attributes, and sanitizing the result again changes nothing.
- Two new packages, each one class over the parser, for those who look for these by name:
  `z3l.FastHtmlParser.Readability` (`Readability.Parse(html, url)`, `Readability.IsProbablyReadable(html)`) and
  `z3l.FastHtmlParser.HtmlSanitizer` (`HtmlSanitizer.Sanitize(html, url, options)`).

## 1.7.0 (2026-11-22)

### Added

- .NET 11: both packages have a `net11.0` build, next to `net9.0` and `net10.0`.

### Changed

- `Text()` is the visible text in one line, as jsoup's `text()`: `<br>`, block elements (`p`, `div`, `li`, `tr`,
  `h1`-`h6`, ...) and table cells separate words, inline tags do not. `"line1<br>line2"` is `"line1 line2"` (was
  `"line1line2"`), a table row `<td>Price</td><td>100</td>` is `"Price 100"` (was `"Price100"`), `"a<b>b</b>"` is still
  `"ab"`. Whitespace and line breaks collapse to one space also in a tag without child tags (`<span>a
  b</span>` is
  `"a b"`, was `"a
  b"`); inside `<pre>`, `<listing>`, `<plaintext>` whitespace is kept, as in jsoup, and `textarea`,
  `script`, `style`, `title` are their content as written. `IsText`, `HtmlText.ToText`, XPath string-values,
  `:contains()` and `text:` in queries read the same text. Most HTML libraries (AngleSharp, HtmlAgilityPack,
  BeautifulSoup, lxml, cheerio) join the text nodes as they are (`"line1line2"`); jsoup was the one that separates.
  Cost: `Text()` of a large element or a table about 20-50% more time (a tag lookup after each word); the scrape
  benchmarks did not change.

### Fixed

- Markup whose name starts with `!` or `?` no longer contains the tags after it, in HTML and in XML documents: the
  XML declaration, `<!DOCTYPE>` of an XHTML page, processing instructions (`<?php ... ?>`, `<?xml-stylesheet ...?>`)
  and downlevel-revealed conditionals (`<![if !IE]>`, `<![endif]>`) are leaves, as a browser and an XML parser read
  them, and no end tag is matched to them. Before, in a document that starts with `<?xml` the whole document was
  inside the declaration: absolute XPath (`/urlset/url/loc`, `/html/body/p`) and `:root` found nothing, and
  `Markdown()` of an XHTML page or a sitemap was empty; in HTML, `<?foo?>` or `<![if ...]>` made the following
  siblings its children (`body > p`, `head > title` missed them).
- The error of an XPath cut short (`//a[`) says "unexpected end of the expression", not "unexpected the end of the
  expression".

## 1.6.1 (2026-11-20)

The first release with HTML to Markdown; 1.6.0 was tagged but never published.

### Added

- HTML to Markdown: `doc.Markdown()`, `tag.Markdown()` and `HtmlText.ToMarkdown(html)` give a page or any part of it
  as Markdown (CommonMark with the GitHub tables, strikethrough and task lists): headings, nested lists, links and
  images (absolute when the document has a page URL), tables with `colspan` / `rowspan`, code blocks with their
  language, quotes, definition lists. `MarkdownOptions` leaves links or images out. Only what a browser shows is
  written; a table that lays out a page is written as the blocks in its cells.
  Text is escaped only where Markdown would read it as its own (`snake_case`, `2 * 3` and `[1]` stay as they are), and
  emphasis that Markdown would not take for emphasis (`**Note:**text`) gets an empty comment next to it.
  One pass over the parsed tags with no object per tag, linear time on any markup: 10–20x faster than ReverseMarkdown
  and Html2Markdown on typical pages (up to 28x on a 510 KB page), with 37–62x less memory.
  Cross-checked on every test page: rendered back to HTML by Markdig, the Markdown has the same text, links, images,
  headings and list items as the page.
- Package `z3l.FastHtmlParser.HtmlToMarkdown`, for those who look for an HTML to Markdown converter: this library
  with one more method, `HtmlToMarkdown.Convert(html, url, options)`, which never returns null. It has the version
  of z3l.FastHtmlParser, depends on it and is released with it; the two can be installed together.

## 1.5.0 (2026-11-20)

### Added

- `tag.Attributes()`: all attributes of a tag in source order, as a browser reads them: names in lower case, values
  decoded, a repeated name kept once (the first). Each `TagAttribute` has `Name`, `Value`, `Body` (the value as written)
  and `HasValue`, and deconstructs: `foreach (var (name, value) in tag.Attributes())`. Known names take no allocation.
  Cross-checked with AngleSharp on every test page.
- Forms: `tag.Form()` (a `<form>`, or the form a control belongs to) and `doc.Forms()` give an `HtmlForm` with
  `Action` (absolute), `Method`, `Enctype`, `Controls()` and `Fields(submitter)`: the name / value pairs a browser sends,
  ready for `new FormUrlEncodedContent(form.Fields())`. Checked and default values, `select` with and without `multiple`,
  disabled controls and fieldsets, radio groups, `form="id"` and the clicked submit button (an ASP.NET postback with
  `__VIEWSTATE`) follow the HTML standard. Cross-checked with AngleSharp's form submission; where AngleSharp differs from
  the standard (value sanitization, `form="id"` outside the form, CR LF in every value) the standard is followed.

### Performance

- Parsing makes no object per tag: the parser records the tags as compact structs, and a `ParserTag` is made the first
  time the tag is asked for (the same object every time after). Searches (`Find`, `FindAll`, CSS, XPath) test names on
  the structs and make objects only for the tags they check further. Parsing allocates about half as much (books page
  39 KB → 18 KB) and is 6–10% faster; a big page no longer puts one array on the large object heap.
- XPath: `contains` / `starts-with` / `ends-with` on an attribute, also the class idiom
  `contains(concat(' ', @class, ' '), ' x ')`, test the attribute value in place; `.` steps (`.//a`) cost nothing.
  The books XPath benchmark allocates 62 KB instead of 173 KB.

## 1.4.2 (2026-11-19)

### Changed

- The license text names every port of the library (C#, Go, Rust, Java, JavaScript / TypeScript, both Python
  packages, PHP, Ruby, C and C++) and covers their packages on any registry. The terms are unchanged; no code changes.

## 1.4.1 (2026-11-17)

### Fixed

- XPath steps on nodes inside one another are linear: `//div//span//a` on nested or unclosed markup, and
  `//a/ancestor::*` on many links in one list, collected every node once per input node and removed the duplicates
  afterwards. 20,000 nested tags took 16 s and gigabytes of memory; now 3 ms. Results are unchanged.
- Line breaks in values are read as a browser reads them: CR LF and a lone CR are LF in attribute values
  (`AttributeByName`, `IsClass`, `IsAttribute`, CSS and XPath attribute tests), in the text of `textarea`, `pre`, `script`
  and other tags without child tags, and in XPath text nodes. Pages saved on Windows gave values with `\r`.
  A CR written as `&#13;` stays; `Body()`, `FullSource()` and `AttributeBody()` still return the source as written,
  and `HtmlText.Decode` now also turns CR LF into LF.

## 1.4.0 (2026-11-16)

### Added

- CSS selectors: `Css`, `CssFirst`, `IsCss` and compiled `CssQuery`, as `querySelectorAll`, `querySelector` and
  `matches` in a browser. Selectors Level 3 plus `:is()`, `:where()`, `:has()`, `:not()` with lists,
  `:nth-child(An+B of S)`, the `i` / `s` attribute flags, form and link pseudo-classes and the non-standard `:contains()`.
  A selector may start with `>` for the children of the tag it is called on: `list.Css("> li")`.
- `HtmlParser.Parse(ReadOnlyMemory<char>, url)`: parses text in a `char[]` or a part of a buffer in place, without
  copying it into a string. `HtmlParser.Parse(IMemoryOwner<char>, length, url)` also takes ownership of a pooled buffer
  and returns it on `Dispose()`. `PageSource` of such a document is made on its first use. The constructor is unchanged;
  parsing and queries are as fast as before.

## 1.3.0 (2026-11-15)

Fixes from a review of the parser on hostile and random markup. Parsing speed and memory use are unchanged;
matching by class or id is 10–20% slower, `Parent()` is many times faster.

### Fixed

- Attribute lookup (`AttributeByName`, `IsClass`, `IsId`, the `Find` criteria) no longer takes a value from inside
  the quoted value of another attribute: in `<a title="see id=5" id="real">` the id is `real`.
- Unquoted attribute values keep their `=` (`href=/p?a=1`), and an attribute that follows a closing quote
  without a space (`href="/q"class="y"`) is found.
- Self-closing tags written without a space (`<br/>`, `<hr/>`, `<path/>`, `<item/>` in XML) have their real name.
  Before, the name was `br/` and `Find`, `HtmlTag.Br` and XPath `//br` missed them.
- Parsing time is linear on any markup. Thousands of unclosed tags followed by stray end tags, tags no rule ends,
  nested lists with left-out end tags and self-closed tags before end tags took seconds; now milliseconds.
- `Parent()` no longer walks back over the previous siblings: the parent is recorded while parsing.
- An end tag is matched again when the optional-end-tag rules have ended its tag
  (`<ul><li>one<div><li>two…</div>`): a tag no longer ends after its parent.
- A self-closed tag (`<span/>`) is not ended by an end tag that comes after the end of its parent.
- `TextAfterTagUntilNextOne` no longer throws for a tag that ends with the source (`<!--<`) or is ended by its parent.
- An end tag with whitespace before `>` (`</a    >`) ends its tag whatever its position in the source.

### Changed

- `AttributeHref` and `AttributeSrc` return the link as a browser follows it: character references are decoded
  (`&amp;` and `&#38;` → `&`), percent-escapes are kept as written. `%3f` and `%3d` are no longer turned into
  `?` and `=`: `/files/what%3F.pdf` stays a file name with a question mark.
- `AttributeHrefMailTo` and `AttributeHrefTelephone` decode character references (`mailto:me&#64;example.com`).
- Comments have no attributes: `Find(null, id: "main")` no longer returns `<!-- <div id="main"> -->`.
- Nesting deeper than 512 levels is flattened, as in browsers: a deeper tag is found and has its text,
  but it is not the parent of the tags inside it.
- After `Dispose()` the document and the tags taken from it throw `ObjectDisposedException`. Before, they threw
  other exceptions or returned empty results. Disposing stays optional.

## 1.2.0 (2026-11-14)

- XPath 1.0: `XPath`, `XPathFirst`, `XPathValues`, `XPathValue` and compiled `XPathQuery`.

## 1.1.2 – 1.1.4 (2026-11-10 – 2026-11-13)

- Package title, description and search tags only.

## 1.1.1 (2026-11-08)

- HTML5 optional end tags, `<` in text, truncated pages.

## 1.1.0 (2026-11-08)

- `HtmlTag` or a tag name can be passed wherever a `TagQuery` is expected.

## 1.0.0 (2026-11-08)

- Package icon and more NuGet tags.

## 0.1.0 (2026-11-08)

- First release.
