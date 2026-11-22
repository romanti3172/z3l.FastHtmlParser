# z3l.FastHtmlParser.Readability

Fast article extraction for .NET 9, .NET 10 and .NET 11, by the method of Mozilla's Readability (the reader view
of Firefox): the article of a page without the menus, sidebars, comments, ads and footers around it, as clean HTML,
Markdown or text, with its title, author, date, excerpt, site name and image.

**Website:** https://readability.fasthtmlparser.com (try it on any page) · **Source:** https://github.com/romanti3172/z3l.FastHtmlParser

11–23x faster than SmartReader, with 14–40x less memory.

| Article of a page: HTML → title, metadata, text | z3l.FastHtmlParser | SmartReader (Readability on AngleSharp) |
|---|---:|---:|
| Ars Technica, 56 KB | **255 μs**, 152 KB | 3,454 μs (14x), 2,439 KB (16x) |
| Medium, 120 KB | **628 μs**, 293 KB | 7,005 μs (11x), 7,941 KB (27x) |
| Wikipedia, 244 KB | **2,508 μs**, 1,396 KB | 48,513 μs (19x), 19,932 KB (14x) |
| New York Times, 309 KB | **935 μs**, 623 KB | 15,569 μs (17x), 8,900 KB (14x) |
| The Guardian, 1.1 MB | **824 μs**, 430 KB | 18,705 μs (23x), 17,081 KB (40x) |

Pages from the test set of Mozilla Readability, .NET 10, Ryzen 7 3800X; `ArticleBenchmarks` in
[benchmarks](https://github.com/romanti3172/z3l.FastHtmlParser/tree/main/benchmarks).

## Install

```
dotnet add package z3l.FastHtmlParser.Readability
```

## Use

```csharp
using z3l.FastHtmlParser;

var article = Readability.Parse(html, "https://example.com/news/2026/11/foxes");
if (article != null)
{
    Console.WriteLine(article.Title);          // "Foxes and dogs: a story of a long morning"
    Console.WriteLine(article.Byline);         // "By Jane Doe"
    Console.WriteLine(article.PublishedTime);  // "2026-11-20T08:00:00Z"

    string markdown = article.Markdown;        // for a language model or a search index
    string text = article.TextContent;         // paragraphs and lines
    string clean = article.Content;            // clean HTML, safe to show
}

// a quick test before the work: lists, search results and the like have no article
if (Readability.IsProbablyReadable(html)) { ... }
```

`Parse` returns null for null or empty HTML and for a page in which no text was found.

## What you get

| Property | From |
|---|---|
| `Title` | JSON-LD (`headline` / `name`), `og:title`, `twitter:title`, Dublin Core; otherwise `<title>` without the site name, or the only `<h1>` |
| `Byline` | JSON-LD `author` (all of them), `author` and Dublin Core meta tags; otherwise a byline in the page (`rel="author"`, `itemprop="author"`, `class="byline"`) |
| `Excerpt` | JSON-LD `description`, `og:description`, `description`; otherwise the first paragraph |
| `SiteName` | JSON-LD `publisher.name`, `og:site_name` |
| `PublishedTime` | JSON-LD `datePublished`, `article:published_time`, `parsely-pub-date` (as the page gives it, usually ISO 8601) |
| `Image` | JSON-LD `image`, `og:image`, `twitter:image` (absolute with the page URL) |
| `Language` | `<html lang>` |
| `Content` | the article as HTML: paragraphs, headings, lists, tables of data, links, images, quotes, code; no scripts, styles, classes, ids or forms; links and lazy images (`data-src`) absolute |
| `Markdown`, `TextContent`, `Length` | made from `Content` on first use |

## How it works

The algorithm is Readability's: elements that are hidden or whose class and id say "sidebar", "comment", "footer",
"share" and the like are left out; every paragraph of 25 chars and more gives points to its parent and grandparent,
more for commas and length; an element's points are lowered by the share of link text in it; the best element,
with its siblings that are good enough, is the article; in it, the parts that look like lists of links, forms or ads
are dropped. When the article is shorter than 500 chars, the page is read again with fewer rules.

Unlike Readability (and its .NET port SmartReader on AngleSharp), the page is not changed: the parser's flat list
of tags is read as it is, what Readability removes is only marked, the text of every element is counted in one pass,
and the article is written by the HTML sanitizer of the parser, so `Content` is always safe to show.

On the 130 test pages of Mozilla Readability the text of the article is the expected one at 0.99 (mean F1 by words),
and 498 of 502 metadata fields are the same.

## With the parser

The package is [z3l.FastHtmlParser](https://www.nuget.org/packages/z3l.FastHtmlParser) with one more class:
the parser comes with it, with its CSS selectors, XPath, Markdown and sanitizer.

```csharp
var doc = new HtmlParser(html, url);

var article = doc.Article();                          // the same as Readability.Parse(html, url)
string? price = doc.CssFirst("span.price").Text();    // and anything else from the same parse
```

Already using z3l.FastHtmlParser? You do not need this package: `Article()` is a part of the parser.

## License

See [LICENSE.md](https://github.com/romanti3172/z3l.FastHtmlParser/blob/main/LICENSE.md). The algorithm follows
Mozilla Readability (Apache License 2.0); no code of it is included.
