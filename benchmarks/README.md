# Benchmarks

z3l.FastHtmlParser 0.1.0 compared with [AngleSharp](https://www.nuget.org/packages/AngleSharp) 1.10.0
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
| quotes, 11 KB | **z3l.FastHtmlParser** | **15.7 μs** | **1.00** | **10.8 KB** | **1.00** |
| | HtmlAgilityPack | 150.5 μs | 9.6x | 243.1 KB | 22.5x |
| | AngleSharp | 228.9 μs | 14.6x | 188.8 KB | 17.5x |
| laptops, 38 KB | **z3l.FastHtmlParser** | **51.9 μs** | **1.00** | **30.7 KB** | **1.00** |
| | HtmlAgilityPack | 566.1 μs | 10.9x | 712.9 KB | 23.2x |
| | AngleSharp | 935.2 μs | 18.0x | 608.4 KB | 19.8x |
| books, 51 KB | **z3l.FastHtmlParser** | **55.0 μs** | **1.00** | **39.2 KB** | **1.00** |
| | HtmlAgilityPack | 618.2 μs | 11.2x | 812.4 KB | 20.7x |
| | AngleSharp | 997.2 μs | 18.1x | 663.4 KB | 16.9x |
| books x10, 510 KB | **z3l.FastHtmlParser** | **546.1 μs** | **1.00** | **391.1 KB** | **1.00** |
| | AngleSharp | 12,273.6 μs | 22.5x | 6,277.2 KB | 16.1x |
| | HtmlAgilityPack | 19,651.9 μs | 36.0x | 8,062.5 KB | 20.6x |

## Scrape a page: parse and extract the records

The setup checks that all three libraries return exactly the same records.

- **Books** (books.toscrape.com, 51 KB): 20 books → title, price, rating.
- **Quotes** (quotes.toscrape.com, 11 KB): 10 quotes → text, author, tags.

AngleSharp uses CSS selectors, HtmlAgilityPack XPath, z3l.FastHtmlParser its `Find*` methods.

| Task | Library | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| Books | **z3l.FastHtmlParser** | **65.1 μs** | **1.00** | **46.6 KB** | **1.00** |
| | HtmlAgilityPack | 799.1 μs | 12.3x | 1,038.2 KB | 22.3x |
| | AngleSharp | 1,016.4 μs | 15.6x | 707.3 KB | 15.2x |
| Quotes | **z3l.FastHtmlParser** | **21.1 μs** | **1.00** | **17.4 KB** | **1.00** |
| | HtmlAgilityPack | 248.1 μs | 11.8x | 401.1 KB | 23.1x |
| | AngleSharp | 271.6 μs | 12.9x | 228.4 KB | 13.2x |

## What is compared

z3l.FastHtmlParser does less than a full DOM parser: it does not build a tree with implicit elements
(`<tbody>`, `<html>`, `<head>`), does not support CSS selectors or XPath, and its documents are read-only.
For extracting data from pages, the results are the same: on the test pages it finds the same elements,
texts, attribute values and parents as AngleSharp (see `AngleSharpCrossCheckTests`).
