# Test fixtures

Copies of pages from public sandboxes made for practicing web scraping, saved on 2026-11-07
so the tests do not depend on the network or on changes of the sites.

| File | Source |
|---|---|
| `quotes/page1.html` | https://quotes.toscrape.com/ |
| `quotes/page2.html` | https://quotes.toscrape.com/page/2/ |
| `quotes/author-einstein.html` | https://quotes.toscrape.com/author/Albert-Einstein/ |
| `quotes/tag-love.html` | https://quotes.toscrape.com/tag/love/ |
| `quotes/tableful.html` | https://quotes.toscrape.com/tableful/ |
| `quotes/login.html` | https://quotes.toscrape.com/login |
| `books/index.html` | https://books.toscrape.com/ |
| `books/category-mystery.html` | https://books.toscrape.com/catalogue/category/books/mystery_3/index.html |
| `books/product-light-in-the-attic.html` | https://books.toscrape.com/catalogue/a-light-in-the-attic_1000/index.html |
| `webscraper/allinone.html` | https://webscraper.io/test-sites/e-commerce/allinone |
| `webscraper/laptops.html` | https://webscraper.io/test-sites/e-commerce/static/computers/laptops |
| `webscraper/product-31.html` | https://webscraper.io/test-sites/e-commerce/static/product/31 |
| `webscraper/tables.html` | https://webscraper.io/test-sites/tables |
| `webscraper/pagination.html` | https://webscraper.io/test-sites/pagination |

The expected values in the tests were taken from the raw HTML, not from the parser's own output.
`AngleSharpCrossCheckTests` additionally compares every page with AngleSharp (a browser-grade parser).
