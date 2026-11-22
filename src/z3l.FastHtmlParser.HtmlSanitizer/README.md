# z3l.FastHtmlParser.HtmlSanitizer

Fast HTML sanitizer for .NET 9, .NET 10 and .NET 11 against XSS: keeps only the allowed tags, attributes, URL schemes
and CSS properties of untrusted HTML (comments, posts, e-mails, scraped pages). No AngleSharp dependency.

**Website:** https://fasthtmlparser.com · **Source:** https://github.com/romanti3172/z3l.FastHtmlParser

8–18x faster than HtmlSanitizer (Ganss.Xss), with 11–37x less memory.

| Sanitize with the default options | z3l.FastHtmlParser | HtmlSanitizer (Ganss.Xss, on AngleSharp) |
|---|---:|---:|
| A comment, 1 KB | **7.6 μs**, 3 KB | 135 μs (18x), 115 KB (37x) |
| quotes.toscrape.com, 11 KB | **67 μs**, 32 KB | 793 μs (12x), 698 KB (22x) |
| webscraper.io laptops, 38 KB | **160 μs**, 76 KB | 1,623 μs (10x), 1,055 KB (14x) |
| books.toscrape.com, 51 KB | **276 μs**, 142 KB | 2,263 μs (8x), 1,573 KB (11x) |

.NET 10, Ryzen 7 3800X; `SanitizeBenchmarks` in
[benchmarks](https://github.com/romanti3172/z3l.FastHtmlParser/tree/main/benchmarks).

## Install

```
dotnet add package z3l.FastHtmlParser.HtmlSanitizer
```

## Use

```csharp
using z3l.FastHtmlParser;

string safe = HtmlSanitizer.Sanitize("<p onclick=\"steal()\">Hi <script>alert(1)</script><a href=\"javascript:x()\">me</a></p>");
// "<p>Hi <a>me</a></p>"

// relative links made absolute, every link with rel="nofollow noopener", no inline styles, YouTube embeds allowed
var options = new SanitizeOptions { LinkRel = "nofollow noopener" };
options.AllowedAttributes.Remove("style");
options.AllowedTags.Add("iframe");
options.AllowedAttributes.Add("allowfullscreen");
string comment = HtmlSanitizer.Sanitize(html, "https://example.com/post/1", options);
```

The result is never null: null or empty HTML gives an empty string.

## What is kept

By default (every set of `SanitizeOptions` can be changed in place):

- **Tags:** text, headings, lists, tables, links, images, quotes, code and the page sections (`p`, `b`, `i`, `a`,
  `img`, `ul`, `table`, `blockquote`, `pre`, `article`, `section`, ...). Not forms, frames, media or scripts.
- **Attributes:** `href`, `src`, `srcset`, `alt`, `title`, `class`, `id`, `style`, the table and list attributes, and
  the old presentation ones (`align`, `width`, `color`, ...). `data-*` with `AllowDataAttributes`.
- **URL schemes:** `http`, `https`, `mailto`, `tel` and relative URLs; `data:image/...` in `img src` with
  `AllowDataImages`. Made absolute when the page URL is given.
- **CSS:** colors, fonts, text alignment, margins, padding, borders and sizes, with plain values only.

A tag that is not allowed is left out and its text stays; `script`, `style`, form controls, frames, media players
and the like go with their content (`RemovedWithContent`).

## What is never kept

Whatever the options say: event handlers (`on*`), `srcdoc`, and the tags `script`, `style`, `template`, `noscript`,
`noembed`, `noframes`, `xmp`, `plaintext`, `base`, `meta`, `link`, `frame`, `frameset`, `svg` and `math`, whose content
a browser does not read as HTML. A URL is dropped when its scheme is not allowed, however it is hidden
(`JaVaScRiPt:`, `jav&#x09;ascript:`, leading control chars, entities); a CSS value with `url(`, `expression`,
`image(`, a backslash or a comment is dropped with its property.

## How it is safe

The result is written anew, never copied from the input: each kept tag by its lower-case name with its kept
attributes in double quotes, every text and attribute value decoded and escaped again, comments and broken markup
dropped, every opened element closed, and the end tags a browser would imply (a `<div>` ends an open `<p>`) written
where it would imply them. So a browser reads the result exactly as it was written, whatever the input was.

The tests parse the result of random broken markup with AngleSharp, which follows the HTML standard as browsers do:
a browser sees only allowed elements and attributes in it, no script URL, and sanitizing the result again changes
nothing.

## With the parser

The package is [z3l.FastHtmlParser](https://www.nuget.org/packages/z3l.FastHtmlParser) with one more class:
the parser comes with it, with its CSS selectors, XPath, Markdown and article extraction.

```csharp
var doc = new HtmlParser(html, url);

string safe = doc.Sanitize(options);                      // the same as HtmlSanitizer.Sanitize(html, url, options)
string? post = doc.CssFirst("div.post-body").Sanitize();  // one part of the page
```

Already using z3l.FastHtmlParser? You do not need this package: `Sanitize()` is a part of the parser.

## License

See [LICENSE.md](https://github.com/romanti3172/z3l.FastHtmlParser/blob/main/LICENSE.md).
