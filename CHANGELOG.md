# Changelog

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
