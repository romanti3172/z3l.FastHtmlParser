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
