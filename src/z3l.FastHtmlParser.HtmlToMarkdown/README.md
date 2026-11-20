# z3l.FastHtmlParser.HtmlToMarkdown

Fast HTML to Markdown for .NET 9 and .NET 10: for a language model, a search index, a note to keep.

**Website:** https://htmltomarkdown.fasthtmlparser.com (try it on any page) · **Source:** https://github.com/romanti3172/z3l.FastHtmlParser

10–20x faster than ReverseMarkdown and Html2Markdown on typical pages, with 37–62x less memory.

| books.toscrape.com, 51 KB: HTML → Markdown | Mean | Allocated |
|---|---:|---:|
| **z3l.FastHtmlParser.HtmlToMarkdown** | **114 μs** | **39 KB** |
| Html2Markdown | 1,751 μs (15x) | 1,627 KB (42x) |
| ReverseMarkdown | 2,195 μs (19x) | 2,240 KB (58x) |
| html-to-markdown (a Rust library, through its .NET binding) | 3,675 μs (32x) | 122 KB (3x) |

All four pages and what is compared:
[benchmarks](https://github.com/romanti3172/z3l.FastHtmlParser/blob/main/benchmarks/README.md#convert-a-page-to-markdown).

## Install

```
dotnet add package z3l.FastHtmlParser.HtmlToMarkdown
```

## Use

```csharp
using z3l.FastHtmlParser;

string markdown = HtmlToMarkdown.Convert(html);

// with the address of the page the links and images are absolute
string page = HtmlToMarkdown.Convert(html, "https://example.com/blog/post");

// only the text and its structure: less to read for a language model
string text = HtmlToMarkdown.Convert(html, options: new MarkdownOptions { Links = false, Images = false });
```

`<h2>Title</h2><p>Some <b>bold</b> text</p>` gives `## Title`, an empty line and `Some **bold** text`.
The result is never null: HTML that is null, empty or shows nothing gives an empty string.

## A part of the page

The package is [z3l.FastHtmlParser](https://www.nuget.org/packages/z3l.FastHtmlParser) with one more method:
the parser comes with it, with its CSS selectors and XPath. Parse the page and take the Markdown of what you need.

```csharp
var doc = new HtmlParser(html, "https://example.com/blog/post");

string? article = doc.CssFirst("article").Markdown();     // the article without the menu and the footer
string? specs = doc.CssFirst("table.specs").Markdown();   // "| Name | Value |\n| --- | --- |\n| ..."
string? title = doc.Find(HtmlTag.H1).Text();
```

Already using z3l.FastHtmlParser? You do not need this package: `Markdown()` is a part of the parser, and
`HtmlToMarkdown.Convert(html, url)` is `new HtmlParser(html, url).Markdown()`.

## What is converted

- Headings, paragraphs, line breaks, emphasis, strikethrough, quotes, rules, nested lists (numbered from their `start`,
  task lists with checkboxes), definition lists, code spans, code blocks with their language (`class="language-…"`),
  links and images with their titles.
- A table of data becomes a GitHub table: `colspan` and `rowspan` keep the columns in place, and a table without
  a heading row gets an empty one. A table that only lays out a page (a table inside it, a single column,
  `role="presentation"`) is written as the blocks in its cells.
- Only what a browser shows: `head`, `script`, `style`, `noscript`, `template`, comments, `svg` and form controls
  are left out.
- Links and images are absolute when the page URL is given. An image that a script loads on scroll is taken from
  `data-src`; a link without an address or with a script is written as its text.
- Text gets a backslash only where Markdown would read it as its own: `snake_case`, `2 * 3`, `[1]` and `AT&T` stay as
  they are; `*text*`, or a line that starts with `#`, `-` or `1.`, is escaped.
- The Markdown renders as the page reads. Emphasis that Markdown would not take for emphasis (`**Note:**text`,
  `文**「字」**文`) gets an empty comment next to it, two lists in a row get different markers, a link around
  several blocks becomes a link in each of them.
- One pass over the parsed tags, no object per tag, linear time on any markup. Lines end with LF; the result has
  no line break at its start or end.

On every test page the Markdown, rendered back to HTML by Markdig, has the same text, links, images, headings
and list items as the page itself.

## License

Free for individuals and organizations with annual revenue **below USD 1,000,000** and
**fewer than 100** employees, including commercial use. Organizations with annual revenue of
USD 1,000,000 or more, or with 100 or more employees, need a commercial license: a **one-time payment of USD 1,000**
per organization, perpetual, all versions. The same license covers every z3l library: you pay once.
See [LICENSE.md](https://github.com/romanti3172/z3l.FastHtmlParser/blob/main/LICENSE.md) for the full terms and
[pricing](https://fasthtmlparser.com/#pricing) to buy a commercial license.
