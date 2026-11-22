# Benchmarks

z3l.FastHtmlParser 1.4.0 compared with [AngleSharp](https://www.nuget.org/packages/AngleSharp) 1.10.0
and [HtmlAgilityPack](https://www.nuget.org/packages/HtmlAgilityPack) 1.13.1 on real pages of
quotes.toscrape.com, books.toscrape.com and webscraper.io/test-sites.

```
dotnet run -c Release --project benchmarks/z3l.FastHtmlParser.Benchmarks -- --filter '*'
```

BenchmarkDotNet 0.15.8, .NET 10.0.12, Intel Xeon E-2236 3.40 GHz, Windows Server 2025.
Ratio and Alloc Ratio: relative to z3l.FastHtmlParser (lower is better).

## Parse a page

Each library parses the page into its own queryable form (AngleSharp and HtmlAgilityPack build a DOM,
z3l.FastHtmlParser a flat list of tags).

| Page | Library | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| quotes, 11 KB | **z3l.FastHtmlParser** | **17.2 μs** | **1.00** | **10.8 KB** | **1.00** |
| | HtmlAgilityPack | 154.6 μs | 9.0x | 243.1 KB | 22.4x |
| | AngleSharp | 229.0 μs | 13.3x | 188.8 KB | 17.4x |
| laptops, 38 KB | **z3l.FastHtmlParser** | **52.5 μs** | **1.00** | **30.7 KB** | **1.00** |
| | HtmlAgilityPack | 544.2 μs | 10.4x | 712.9 KB | 23.2x |
| | AngleSharp | 959.0 μs | 18.3x | 608.4 KB | 19.8x |
| books, 51 KB | **z3l.FastHtmlParser** | **58.6 μs** | **1.00** | **39.2 KB** | **1.00** |
| | HtmlAgilityPack | 609.6 μs | 10.4x | 812.4 KB | 20.7x |
| | AngleSharp | 958.4 μs | 16.4x | 663.4 KB | 16.9x |
| books x10, 510 KB | **z3l.FastHtmlParser** | **609.8 μs** | **1.00** | **391.1 KB** | **1.00** |
| | AngleSharp | 12,201.6 μs | 20.0x | 6,276.9 KB | 16.1x |
| | HtmlAgilityPack | 19,550.0 μs | 32.1x | 8,062.5 KB | 20.6x |
| books without `</li>` and `</p>`, 51 KB | **z3l.FastHtmlParser** | **58.5 μs** | **1.00** | **39.2 KB** | **1.00** |
| | HtmlAgilityPack | 670.9 μs | 11.5x | 756.2 KB | 19.3x |
| | AngleSharp | 949.4 μs | 16.2x | 643.7 KB | 16.4x |

## Scrape a page: parse and extract the records

The setup checks that all libraries and all variants return exactly the same records.

- **Books** (books.toscrape.com, 51 KB): 20 books → title, price, rating.
- **Quotes** (quotes.toscrape.com, 11 KB): 10 quotes → text, author, tags.

AngleSharp uses CSS selectors, HtmlAgilityPack XPath, z3l.FastHtmlParser its `Find*` methods; the "XPath" row
runs the same XPath expressions as HtmlAgilityPack, the "CSS" rows the same selectors as AngleSharp.

| Task | Library | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| Books | **z3l.FastHtmlParser** | **69.0 μs** | **1.00** | **44.8 KB** | **1.00** |
| | z3l.FastHtmlParser CSS | 75.2 μs | 1.1x | 45.2 KB | 1.0x |
| | z3l.FastHtmlParser XPath | 109.4 μs | 1.6x | 173.4 KB | 3.9x |
| | HtmlAgilityPack | 760.4 μs | 11.0x | 1,038.2 KB | 23.2x |
| | AngleSharp | 1,042.9 μs | 15.1x | 707.3 KB | 15.8x |
| Quotes | **z3l.FastHtmlParser** | **23.5 μs** | **1.00** | **18.4 KB** | **1.00** |
| | z3l.FastHtmlParser CSS | 25.7 μs | 1.1x | 17.7 KB | 1.0x |
| | HtmlAgilityPack | 246.7 μs | 10.5x | 401.1 KB | 21.8x |
| | AngleSharp | 267.0 μs | 11.4x | 228.4 KB | 12.4x |

With CSS selectors the same task is about 14x faster than AngleSharp running the same selectors (books: 75 μs
against 1,043 μs), with 16x less memory.

## Convert a page to Markdown

From the HTML string to the Markdown string, parsing included: `new HtmlParser(html).Markdown()` against
[ReverseMarkdown](https://www.nuget.org/packages/ReverseMarkdown) 6.3.0 and
[Html2Markdown](https://www.nuget.org/packages/Html2Markdown) 8.1.2, which both build an HtmlAgilityPack document first,
and [html-to-markdown](https://www.nuget.org/packages/KreuzbergDev.HtmlToMarkdown) 3.7.2, a Rust library called through
its .NET binding with its default options.

```
dotnet run -c Release --project benchmarks/z3l.FastHtmlParser.Benchmarks -- --filter '*MarkdownBenchmarks*'
```

BenchmarkDotNet 0.15.8, .NET 10.0.12, AMD Ryzen 7 3800X 3.90 GHz, Windows 10 (another machine than the tables above:
compare the ratios, not the times).

| Page | Library | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| quotes, 11 KB | **z3l.FastHtmlParser** | **38.7 μs** | **1.00** | **10.8 KB** | **1.00** |
| | Html2Markdown | 389.2 μs | 10.1x | 443.9 KB | 41.2x |
| | ReverseMarkdown | 455.6 μs | 11.8x | 397.9 KB | 36.9x |
| | html-to-markdown (Rust) | 627.7 μs | 16.3x | 58.1 KB | 5.4x |
| laptops, 38 KB | **z3l.FastHtmlParser** | **87.0 μs** | **1.00** | **22.7 KB** | **1.00** |
| | ReverseMarkdown | 1,487.5 μs | 17.1x | 1,286.4 KB | 56.6x |
| | Html2Markdown | 1,564.9 μs | 18.0x | 1,414.5 KB | 62.3x |
| | html-to-markdown (Rust) | 3,039.6 μs | 35.0x | 85.4 KB | 3.8x |
| books, 51 KB | **z3l.FastHtmlParser** | **113.6 μs** | **1.00** | **38.6 KB** | **1.00** |
| | Html2Markdown | 1,751.4 μs | 15.4x | 1,626.9 KB | 42.1x |
| | ReverseMarkdown | 2,194.8 μs | 19.4x | 2,240.4 KB | 58.0x |
| | html-to-markdown (Rust) | 3,675.4 μs | 32.4x | 121.7 KB | 3.2x |
| books x10, 510 KB | **z3l.FastHtmlParser** | **1,214.2 μs** | **1.00** | **384.5 KB** | **1.00** |
| | Html2Markdown | 22,054.6 μs | 18.2x | 14,712.9 KB | 38.3x |
| | ReverseMarkdown | 33,468.8 μs | 27.6x | 20,662.3 KB | 53.7x |
| | html-to-markdown (Rust) | 37,669.4 μs | 31.0x | 1,179.0 KB | 3.1x |

About half of the time of z3l.FastHtmlParser is the parsing (books: about 55 μs of 114); the conversion is one more
pass over the parsed tags, and what it allocates is its result (books: 21 KB for about 10,500 chars of Markdown).

The libraries do not give the same text: each has its own idea of what Markdown a page is. For these pages
ReverseMarkdown writes nearly the same Markdown as z3l.FastHtmlParser (quotes: 3,016 chars against 2,964). Html2Markdown
converts the tags it knows and leaves the others (`div`, `span`, `meta`) in its result as HTML, so its result is
longer. The setup only checks that no library returns much less than the others. The Rust library works in native
memory, which the Allocated column does not show: its numbers there are what its binding allocates.

Whether the Markdown is right is not measured here but tested: `MarkdownCrossCheckTests` renders the Markdown of every
test page back to HTML with Markdig and compares it with the page (text, links, images, headings, list items).

## Extract the article of a page

From the HTML string to the article with its metadata and text: `new HtmlParser(html, url).Article()` against
[SmartReader](https://www.nuget.org/packages/SmartReader) 0.11.1, the .NET port of Mozilla Readability on AngleSharp
(`new Reader(url, html).GetArticle()`). The pages are test pages of Mozilla Readability
(`test/test-pages/*/source.html`), saved in `benchmarks/local` as `article-*.html`: third-party content, not in the
repository. The setup checks that both find an article of about the same length.

```
dotnet run -c Release --project benchmarks/z3l.FastHtmlParser.Benchmarks -- --filter '*ArticleBenchmarks*'
```

BenchmarkDotNet 0.15.8, .NET 10.0.12, AMD Ryzen 7 3800X 3.90 GHz, Windows 10.

| Page | Library | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| Ars Technica, 56 KB | **z3l.FastHtmlParser** | **254.9 μs** | **1.00** | **151.7 KB** | **1.00** |
| | SmartReader | 3,454.2 μs | 13.6x | 2,438.6 KB | 16.1x |
| Medium, 120 KB | **z3l.FastHtmlParser** | **628.1 μs** | **1.00** | **292.5 KB** | **1.00** |
| | SmartReader | 7,004.6 μs | 11.2x | 7,941.2 KB | 27.1x |
| Wikipedia, 244 KB | **z3l.FastHtmlParser** | **2,508.2 μs** | **1.00** | **1,396.4 KB** | **1.00** |
| | SmartReader | 48,513.3 μs | 19.3x | 19,931.7 KB | 14.3x |
| New York Times, 309 KB | **z3l.FastHtmlParser** | **935.1 μs** | **1.00** | **622.8 KB** | **1.00** |
| | SmartReader | 15,569.4 μs | 16.6x | 8,899.8 KB | 14.3x |
| The Guardian, 1.1 MB | **z3l.FastHtmlParser** | **824.0 μs** | **1.00** | **430.3 KB** | **1.00** |
| | SmartReader | 18,705.1 μs | 22.7x | 17,081.4 KB | 39.7x |

The time of z3l.FastHtmlParser grows with the markup, that of SmartReader with the DOM it builds and changes: The
Guardian is 1.1 MB, but mostly scripts and styles, which the parser passes over as text.

## Sanitize HTML

From the HTML string to the safe HTML string, with the default options of both: `new HtmlParser(html).Sanitize()`
against [HtmlSanitizer](https://www.nuget.org/packages/HtmlSanitizer) 11.0.26 (Ganss.Xss), which parses with AngleSharp
and writes the DOM back. "a comment" is 1 KB of a user comment with the usual tricks in it (event handlers,
`javascript:` links, a script, an `svg`, an `iframe`); the other pages are whole pages.

```
dotnet run -c Release --project benchmarks/z3l.FastHtmlParser.Benchmarks -- --filter '*SanitizeBenchmarks*'
```

BenchmarkDotNet 0.15.8, .NET 10.0.12, AMD Ryzen 7 3800X 3.90 GHz, Windows 10.

| Page | Library | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| a comment, 1 KB | **z3l.FastHtmlParser** | **7.6 μs** | **1.00** | **3.1 KB** | **1.00** |
| | HtmlSanitizer (Ganss.Xss) | 135.2 μs | 17.8x | 114.7 KB | 37.0x |
| quotes, 11 KB | **z3l.FastHtmlParser** | **67.4 μs** | **1.00** | **31.9 KB** | **1.00** |
| | HtmlSanitizer (Ganss.Xss) | 793.0 μs | 11.8x | 697.9 KB | 21.9x |
| laptops, 38 KB | **z3l.FastHtmlParser** | **159.9 μs** | **1.00** | **75.6 KB** | **1.00** |
| | HtmlSanitizer (Ganss.Xss) | 1,623.1 μs | 10.2x | 1,054.9 KB | 14.0x |
| books, 51 KB | **z3l.FastHtmlParser** | **275.8 μs** | **1.00** | **142.3 KB** | **1.00** |
| | HtmlSanitizer (Ganss.Xss) | 2,262.8 μs | 8.2x | 1,573.2 KB | 11.1x |

## What is compared

The books page without end tags is the books page with all 75 `</li>` and 60 `</p>` left out, as on sloppy sites:
the optional-end-tag rules cost nothing measurable there (58.5 μs against 58.6 μs for the original page).

On a real 1 MB Amazon search results page (saved locally, not in the repository: see `LocalScrapeBenchmarks`)
parsing takes 0.65 ms against 11.3 ms (HtmlAgilityPack) and 15.1 ms (AngleSharp), and extracting 27 products
(ASIN, price, name, rating, Prime) 0.87 ms against 16.3 ms and 18.1 ms, with 23–29x less memory.

z3l.FastHtmlParser does less than a full DOM parser: it does not build a tree with implicit elements
(`<tbody>`, `<html>`, `<head>`), and its documents are read-only. It does apply the HTML5 optional-end-tag rules
(`<li>`, `<p>`, `<td>`, `<tr>`, `<dt>`/`<dd>`, `<option>`), so pages with left-out end tags give the same elements
and texts. For extracting data from pages, the results are the same: on the test pages and on a set of broken-markup
snippets it finds the same elements, texts, attribute values and parents as AngleSharp (see `AngleSharpCrossCheckTests`),
and CSS selectors select the same elements (see `CssTests`).

## JIT, NativeAOT and server GC

`--runtime-variants` runs each benchmark four times: JIT and NativeAOT, each with workstation and server GC.

```
dotnet run -c Release --project benchmarks/z3l.FastHtmlParser.Benchmarks -- --runtime-variants --filter '*ParseBenchmarks.FastHtmlParser' '*ScrapeBenchmarks.*_FastHtmlParser*'
```

NativeAOT needs the platform linker: on Linux `clang` and `zlib1g-dev`; on Windows the Visual Studio C++ build tools,
with `C:\Program Files (x86)\Microsoft Visual Studio\Installer` (where `vswhere.exe` lives) on `PATH`.
BenchmarkDotNet compiles NativeAOT for the instruction set of the machine it runs on, as the JIT does.

On an AMD Ryzen 7 3800X (.NET 10.0.12) NativeAOT was 20–50% slower than the JIT on every benchmark, on Linux and on
Windows, and server GC 2–15% slower than workstation GC (up to 24% on XPath, which allocates the most): the JIT's dynamic
PGO matters more here than ahead-of-time compilation, and a single-threaded parser gains nothing from server GC.
