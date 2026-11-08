# Benchmarks

z3l.FastHtmlParser 1.1.1 compared with [AngleSharp](https://www.nuget.org/packages/AngleSharp) 1.10.0
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
| quotes, 11 KB | **z3l.FastHtmlParser** | **19.1 μs** | **1.00** | **10.8 KB** | **1.00** |
| | HtmlAgilityPack | 173.8 μs | 9.2x | 243.1 KB | 22.5x |
| | AngleSharp | 260.4 μs | 13.7x | 188.8 KB | 17.5x |
| laptops, 38 KB | **z3l.FastHtmlParser** | **56.9 μs** | **1.00** | **30.7 KB** | **1.00** |
| | HtmlAgilityPack | 583.8 μs | 10.3x | 712.9 KB | 23.2x |
| | AngleSharp | 1,096.4 μs | 19.3x | 608.4 KB | 19.8x |
| books, 51 KB | **z3l.FastHtmlParser** | **61.0 μs** | **1.00** | **39.2 KB** | **1.00** |
| | HtmlAgilityPack | 625.8 μs | 10.3x | 812.4 KB | 20.7x |
| | AngleSharp | 1,087.5 μs | 17.8x | 663.4 KB | 16.9x |
| books x10, 510 KB | **z3l.FastHtmlParser** | **662.9 μs** | **1.00** | **391.1 KB** | **1.00** |
| | AngleSharp | 13,817.7 μs | 20.9x | 6,277.2 KB | 16.1x |
| | HtmlAgilityPack | 20,873.1 μs | 31.5x | 8,062.5 KB | 20.6x |
| books without `</li>` and `</p>`, 51 KB | **z3l.FastHtmlParser** | **63.4 μs** | **1.00** | **39.2 KB** | **1.00** |
| | HtmlAgilityPack | 728.5 μs | 11.5x | 756.2 KB | 19.3x |
| | AngleSharp | 1,044.4 μs | 16.5x | 643.7 KB | 16.4x |

## Scrape a page: parse and extract the records

The setup checks that all three libraries return exactly the same records.

- **Books** (books.toscrape.com, 51 KB): 20 books → title, price, rating.
- **Quotes** (quotes.toscrape.com, 11 KB): 10 quotes → text, author, tags.

AngleSharp uses CSS selectors, HtmlAgilityPack XPath, z3l.FastHtmlParser its `Find*` methods.

| Task | Library | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| Books | **z3l.FastHtmlParser** | **71.4 μs** | **1.00** | **44.8 KB** | **1.00** |
| | HtmlAgilityPack | 861.7 μs | 12.1x | 1,038.2 KB | 23.2x |
| | AngleSharp | 1,160.4 μs | 16.3x | 707.3 KB | 15.8x |
| Quotes | **z3l.FastHtmlParser** | **24.1 μs** | **1.00** | **18.4 KB** | **1.00** |
| | HtmlAgilityPack | 257.0 μs | 10.7x | 401.1 KB | 21.8x |
| | AngleSharp | 292.1 μs | 12.2x | 228.4 KB | 12.4x |

## What is compared

The last page is the books page with all 75 `</li>` and 60 `</p>` left out, as on sloppy sites: the optional-end-tag
rules cost next to nothing there (63 μs against 61 μs for the original page).

On a real 1 MB Amazon search results page (saved locally, not in the repository: see `LocalScrapeBenchmarks`)
parsing takes 0.72 ms against 12.3 ms (HtmlAgilityPack) and 16.3 ms (AngleSharp), and extracting 27 products
(ASIN, price, name, rating, Prime) 0.90 ms against 18.0 ms and 19.8 ms, with 23–29x less memory.

z3l.FastHtmlParser does less than a full DOM parser: it does not build a tree with implicit elements
(`<tbody>`, `<html>`, `<head>`), does not support CSS selectors or XPath, and its documents are read-only.
It does apply the HTML5 optional-end-tag rules (`<li>`, `<p>`, `<td>`, `<tr>`, `<dt>`/`<dd>`, `<option>`),
so pages with left-out end tags give the same elements and texts. For extracting data from pages, the results
are the same: on the test pages and on a set of broken-markup snippets it finds the same elements, texts,
attribute values and parents as AngleSharp (see `AngleSharpCrossCheckTests`).
