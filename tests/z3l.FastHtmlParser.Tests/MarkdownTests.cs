namespace z3l.FastHtmlParser.Tests;

/// <summary>
/// HTML → Markdown: what every kind of element gives, what is escaped, the options and the API.
/// <see cref="MarkdownCrossCheckTests"/> renders the result of every <c>Converts_</c> case back to HTML.
/// </summary>
[TestClass]
public sealed class MarkdownTests
{
    [TestMethod]
    [DataRow("  lots   of\n\n spaces \r\n and\tnewlines  ", "lots of spaces and newlines")]
    [DataRow("<p>First</p><p>Second</p>", "First\n\nSecond")]
    [DataRow("<P class='a'>Upper</P><p>lower</p>", "Upper\n\nlower")]
    [DataRow("<p>a</p><p>&nbsp;</p><p>b</p>", "a\n\nb")]
    [DataRow("<div>One</div><div>Two</div><span>inline</span> <span>spans</span>", "One\n\nTwo\n\ninline spans")]
    [DataRow("Line one<br>Line two<BR/>Line three<br class='x'>Line four", "Line one  \nLine two  \nLine three  \nLine four")]
    [DataRow("a<br><br><br>b", "a\n\nb")]                                   // two or more <br>: a paragraph
    [DataRow("<p>a<br></p><p><br>b</p><br>c<br>", "a\n\nb\n\nc")]           // a <br> at the edge of a block adds nothing
    [DataRow("<div><div><div>deep</div></div></div><p></p><div> </div><section><article><header>H</header><footer>F</footer></article></section>", "deep\n\nH\n\nF")]
    [DataRow("<p>Text with <span>span</span><span>s</span> and <font>font</font>, <custom-el>custom</custom-el>.</p>", "Text with spans and font, custom.")]
    [DataRow("one<hr>two<hr/>", "one\n\n---\n\ntwo\n\n---")]
    [DataRow("<h2>Title</h2>Body text", "## Title\n\nBody text")]
    public void Converts_ParagraphsAndLineBreaks(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("<h1>Title</h1><h2>Sub</h2><h6>Six</h6>text", "# Title\n\n## Sub\n\n###### Six\n\ntext")]
    [DataRow("<h1>a<br>b</h1>", "# a b")]                                   // a heading is one line
    [DataRow("<h2> spaced \n  out </h2>", "## spaced out")]
    [DataRow("<h2><b>x</b></h2>", "## **x**")]
    [DataRow("<h2>Heading with <a href=\"/a\">link</a> and <code>code</code></h2><h3></h3><h4>C #</h4>", "## Heading with [link](/a) and `code`\n\n#### C \\#")]
    [DataRow("<h3>1. Numbered # and - plain</h3>", "### 1. Numbered # and - plain")]
    [DataRow("<ul><li><h3>in li</h3></li></ul>", "- ### in li")]
    [DataRow("<a href=\"/h\"><h2>linked</h2></a>", "## [linked](/h)")]
    [DataRow("<h2><img src=\"i.png\" alt=\"img\"></h2>", "## ![img](i.png)")]
    [DataRow("<h2><div>block</div><p>inside</p></h2>after", "## block inside\n\nafter")]
    [DataRow("<h7>seven</h7>", "seven")]
    public void Converts_Headings(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("Hello <b>bold</b>, <strong>strong</strong>, <i>italic</i>, <em>em</em>", "Hello **bold**, **strong**, *italic*, *em*")]
    [DataRow("<b>bold <i>both</i></b> <i>a</i><b>b</b> <b>a</b> <b>b</b> <b><b>x</b></b>", "**bold *both*** *a***b** **a** **b** **x**")]
    [DataRow("<p><b> bold </b>text<b>a</b><b>b</b> <i></i><b><i>both</i></b></p>", "**bold** text**ab** ***both***")] // spaces go outside, empty pairs give nothing
    [DataRow("foo<b> bar </b>baz <b>  </b>x<b><br></b>y", "foo **bar** baz x  \ny")]
    [DataRow("<em>a<p>b</p>c</em>", "*a*\n\n*b*\n\n*c*")]                   // emphasis does not reach over blocks
    [DataRow("in<b>tra</b>word", "in**tra**word")]
    [DataRow("<q>q</q> <s>s</s> <strike>strike</strike> <del>del</del> <ins>ins</ins> <u>u</u> <mark>mark</mark> <small>small</small> <var>v</var> <cite>c</cite> <abbr title=\"t\">abbr</abbr>",
        "\"q\" ~~s~~ ~~strike~~ ~~del~~ ins u mark small *v* *c* abbr")]
    [DataRow("H<sub>2</sub>O x<sup>2</sup>", "H<sub>2</sub>O x<sup>2</sup>")]
    public void Converts_Emphasis(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    /// <summary>
    /// Markdown reads "**" as emphasis only when the chars around it allow it: not between a letter and a punctuation char.
    /// There an empty comment is written next to it, which is punctuation and is not shown.
    /// </summary>
    [TestMethod]
    [DataRow("<b>Note:</b> text and <b>Note:</b>text", "**Note:** text and **Note:**<!---->text")]
    [DataRow("foo<b>:</b>bar <b>a</b>b", "foo<!---->**:**<!---->bar **a**b")]
    [DataRow("text<b>(bold)</b> and x<b><a href=\"/u\">link</a></b>y", "text<!---->**(bold)** and x<!---->**[link](/u)**<!---->y")]
    [DataRow("这是<b>「重点」</b>内容", "这是<!---->**「重点」**<!---->内容")]
    [DataRow("<b>foo:</b><i>bar</i> <b><i>x!</i></b>y a<del>(b)</del>c", "**foo:**<!---->*bar* ***x!***<!---->y a<!---->~~(b)~~<!---->c")]
    [DataRow("<p>start <b>\"quoted\"</b> end. <b>!</b> <i>(x)</i>.</p><p><b>(at start)</b>z</p>", "start **\"quoted\"** end. **!** *(x)*.\n\n**(at start)**<!---->z")]
    [DataRow("x<b><code>c</code></b>y", "x<!---->**`c`**<!---->y")]
    public void Converts_EmphasisNextToPunctuation(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("<a href=\"https://example.com/a?b=1&amp;c=2\" title=\"The title\">link</a>.", "[link](https://example.com/a?b=1&c=2 \"The title\").")]
    [DataRow("<a>no href</a> <a href=\"\">empty</a> <a href=\"#\">hash</a> <a href=\"#frag\">frag</a> <a href=\"javascript:void(0)\">js</a> <a href=\"mailto:a@b.c\">mail</a>",
        "no href empty hash [frag](#frag) js [mail](mailto:a@b.c)")]
    [DataRow("<a href=\"/a b/c(d)\">spaces</a> <a href=\"/x)\">paren</a>", "[spaces](/a%20b/c(d)) [paren](/x%29)")]
    [DataRow("<a href=\"x\"><img src=\"i.png\" alt=\"a\"></a> <a href=\"outer\">out <a href=\"inner\">in</a></a> <a href=\"x\"></a>", "[![a](i.png)](x) [out in](outer)")]
    [DataRow("<a href=\"/t\" title='say \"hi\" \\ there'>t</a>", "[t](/t \"say \\\"hi\\\" \\\\ there\")")]
    [DataRow("Hello!<a href=\"/x\">link</a>", "Hello\\![link](/x)")]         // not an image
    [DataRow("<a href=\"/u\">a ] b [ c</a>", "[a \\] b \\[ c](/u)")]
    [DataRow("<a href=\"/u\"> spaced </a>text", "[spaced](/u) text")]
    [DataRow("<a href=\"/card\"><div><h3>Card title</h3><p>Card text</p></div></a>", "### [Card title](/card)\n\n[Card text](/card)")] // a link around blocks: one in each
    public void Converts_Links(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("<img src=\"/i.png\" alt=\"An [alt] text\" title=\"T\">", "![An \\[alt\\] text](/i.png \"T\")")]
    [DataRow("<img src=\"a.png\" alt=\"line\nbreak\"> x <a href=\"/l\"><img src=\"b.png\"></a>", "![line break](a.png) x [![](b.png)](/l)")]
    // images loaded when they scroll into view: the address is in data-src, src is a placeholder; a file packed into the page is left out
    [DataRow("<img data-src=\"lazy.jpg\" src=\"data:image/gif;base64,AAAA\"> <img src=\"data:image/gif;base64,AAAA\"><img alt=\"no src\">", "![](lazy.jpg)")]
    [DataRow("<img src=\"spacer.gif\" data-lazy-src=\"real.jpg\" alt=\"x\"> <img data-original=\"o.jpg\">", "![x](real.jpg) ![](o.jpg)")]
    [DataRow("<picture><source srcset=\"a.webp\"><img src=\"a.jpg\" alt=\"pic\"></picture>", "![pic](a.jpg)")]
    [DataRow("<figure><img src=\"f.png\" alt=\"fig\"><figcaption>Caption</figcaption></figure>", "![fig](f.png)\n\nCaption")]
    public void Converts_Images(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("<ul><li>one</li><li>two<ul><li>nested a</li><li>nested b</li></ul></li><li><p>para one</p><p>para two</p></li></ul><ol start=\"3\"><li>three</li><li>four</li></ol>",
        "- one\n- two\n  - nested a\n  - nested b\n- para one\n\n  para two\n\n3. three\n4. four")]
    [DataRow("<ul><li></li><li>a</li><li><ul><li>only nested</li></ul></li><li><p>p1</p></li><li><p>p2</p></li></ul>", "- a\n- - only nested\n- p1\n- p2")]
    [DataRow("<ol><li>1</li><li>2</li><li>3</li><li>4</li><li>5</li><li>6</li><li>7</li><li>8</li><li>9</li><li>ten<ul><li>nested</li></ul></li></ol>",
        "1. 1\n2. 2\n3. 3\n4. 4\n5. 5\n6. 6\n7. 7\n8. 8\n9. 9\n10. ten\n    - nested")]             // the indent is as wide as the marker
    [DataRow("<ol start=\"0\"><li>zero</li></ol>", "0. zero")]
    [DataRow("<ul><li>item<ul><li>nested</li></ul>text after</li><li>next</li></ul><p>after list</p>", "- item\n  - nested\n\n  text after\n- next\n\nafter list")]
    [DataRow("<ol><li><p>Loose one</p><p>More</p></li><li>Two<ol><li>Sub<ul><li>Leaf</li></ul></li></ol></li></ol>", "1. Loose one\n\n   More\n2. Two\n   1. Sub\n      - Leaf")]
    // two lists of one kind in a row would be read as one: the second has the other marker
    [DataRow("<ul><li>a</li></ul><ul><li>b</li></ul><ul><li>c</li></ul>", "- a\n\n* b\n\n- c")]
    [DataRow("<ol><li>a</li></ol><ol><li>b</li></ol>", "1. a\n\n1) b")]
    [DataRow("<ul><li>a</li></ul><ol><li>b</li></ol><ul><li>c</li></ul><p>x</p><ul><li>d</li></ul>", "- a\n\n1. b\n\n- c\n\nx\n\n- d")]
    [DataRow("<ul><li>a<ul><li>n1</li></ul></li><li><ul><li>n2</li></ul></li></ul>", "- a\n  - n1\n- - n2")]
    [DataRow("<ul><li><input type=\"checkbox\" checked> done</li><li><input type=checkbox>todo</li><li>plain <input type=\"checkbox\"></li></ul>", "- [x] done\n- [ ] todo\n- plain")]
    [DataRow("<li>outside</li><menu><li>menu</li></menu>", "- outside\n\n- menu")]
    [DataRow("<ul><li>1. x</li><li># x</li><li>- x</li></ul>", "- 1\\. x\n- \\# x\n- \\- x")]
    [DataRow("<ul><li>[ ] not a task</li><li>x [x] y</li></ul><p>[ ] plain</p>", "- \\[ ] not a task\n- x \\[x] y\n\n[ ] plain")]
    [DataRow("<dl><dt>Term</dt><dd>Definition</dd><dt>Other</dt><dd>One</dd><dd>Two</dd></dl>", "Term\n: Definition\n\nOther\n: One\n: Two")]
    [DataRow("<ul><li><dl><dt>T</dt><dd>D</dd></dl></li></ul><dl><dt>alone</dt></dl>", "- T\n  : D\n\nalone")]
    public void Converts_Lists(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("<blockquote><p>Quote one</p><p>Quote two</p><ul><li>item</li></ul></blockquote>after", "> Quote one\n>\n> Quote two\n>\n> - item\n\nafter")]
    [DataRow("<blockquote>one<blockquote>two<blockquote>three</blockquote></blockquote><h2>H</h2></blockquote>", "> one\n>\n> > two\n> >\n> > > three\n>\n> ## H")]
    [DataRow("<ul><li>a<blockquote><ul><li>deep</li></ul><p>para</p></blockquote></li></ul>", "- a\n\n  > - deep\n  >\n  > para")]
    [DataRow("<blockquote>&gt; x<br>y</blockquote>", "> \\> x  \n> y")]
    public void Converts_Quotes(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("Text <code>a `b` c</code> and <kbd>Ctrl</kbd> <samp>out</samp> <tt>tt</tt>", "Text ``a `b` c`` and `Ctrl` `out` `tt`")]
    [DataRow("x<code></code>y <code> </code> <code>a\nb</code> <a href=\"/u\"><code>in link</code></a>", "xy `a b` [`in link`](/u)")]
    [DataRow("<code>`tick`</code>", "`` `tick` ``")]
    [DataRow("<code>&lt;b&gt; *not* _md_ &amp;amp; <i>tags</i> gone</code>", "`<b> *not* _md_ &amp; tags gone`")]
    [DataRow("<pre><code class=\"language-csharp\">var x = 1;\nif (x &lt; 2) { }\n</code></pre>", "```csharp\nvar x = 1;\nif (x < 2) { }\n```")]
    [DataRow("<pre>\nfirst\n  second\n\nfourth</pre>", "```\nfirst\n  second\n\nfourth\n```")]
    [DataRow("<pre>a<br>b ``` c</pre>", "````\na\nb ``` c\n````")]
    [DataRow("<pre class=\"lang-js\">let x;</pre><pre class=\"brush language-&quot;x\">y</pre>", "```js\nlet x;\n```\n\n```\ny\n```")]
    [DataRow("<pre><code class=\"hljs language-python\"><span class=\"kw\">def</span> f(): <span>pass</span></code></pre>", "```python\ndef f(): pass\n```")]
    [DataRow("<blockquote><pre>line1\n\nline3</pre></blockquote><ul><li><pre>in li</pre></li></ul>", "> ```\n> line1\n>\n> line3\n> ```\n\n- ```\n  in li\n  ```")]
    [DataRow("<pre>one\r\ntwo\rthree</pre>", "```\none\ntwo\nthree\n```")]
    [DataRow("<pre></pre><pre>  \n </pre>x<xmp><b>raw</b></xmp>", "x\n\n```\n<b>raw</b>\n```")]
    public void Converts_Code(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("<table><thead><tr><th>Name</th><th>Price</th></tr></thead><tbody><tr><td>Tea</td><td>1 | 2</td></tr><tr><td colspan=\"2\">wide</td></tr></tbody></table>",
        "| Name | Price |\n| --- | --- |\n| Tea | 1 \\| 2 |\n| wide |  |")]
    [DataRow("<table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>", "| A | B |\n| --- | --- |\n| 1 | 2 |")]
    // no heading row: an empty one, Markdown has no table without it
    [DataRow("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>", "|  |  |\n| --- | --- |\n| a | b |\n| c | d |")]
    [DataRow("<table><tr><th>UPC</th><td>a897</td></tr><tr><th>Type</th><td>Books</td></tr></table>", "|  |  |\n| --- | --- |\n| UPC | a897 |\n| Type | Books |")]
    [DataRow("<table><caption>The <b>caption</b></caption><thead><tr><td>h1</td><td>h2</td><td>h3</td></tr></thead><tr><td><b>bold</b> <a href=\"/l\">link</a></td><td><code>a|b</code><br>line2</td><td><ul><li>x</li><li>y</li></ul></td></tr><tr><td></td><td><p>p1</p><p>p2</p></td></tr><tfoot><tr><td>f</td></tr></tfoot></table>",
        "The **caption**\n\n| h1 | h2 | h3 |\n| --- | --- | --- |\n| **bold** [link](/l) | `a\\|b`<br>line2 | - x<br>- y |\n|  | p1<br>p2 |  |\n| f |  |  |")]
    // rowspan and colspan: empty cells keep the columns in place
    [DataRow("<table><tr><td rowspan=\"2\">A</td><td>B</td></tr><tr><td>C</td></tr><tr><td>D</td><td>E</td></tr></table>", "|  |  |\n| --- | --- |\n| A | B |\n|  | C |\n| D | E |")]
    [DataRow("<table><tr><th rowspan=\"2\">Name</th><th colspan=\"2\">Size</th></tr><tr><th>W</th><th>H</th></tr><tr><td>Box</td><td>1</td><td>2</td></tr></table>",
        "| Name | Size |  |\n| --- | --- | --- |\n|  | W | H |\n| Box | 1 | 2 |")]
    [DataRow("<table><td>x</td><td>y</td><tr><td>1</td><td>2</td><td>3</td></tr></table>", "|  |  |  |\n| --- | --- | --- |\n| x | y |  |\n| 1 | 2 | 3 |")]
    [DataRow("<table><tr><td>A<td>B<tr><td>C<td>D</table>after", "|  |  |\n| --- | --- |\n| A | B |\n| C | D |\n\nafter")]
    [DataRow("<b><table><tr><th>a</th><th>b</th></tr><tr><td>1</td><td>2</td></tr></table></b>text", "| **a** | **b** |\n| --- | --- |\n| **1** | **2** |\n\ntext")]
    [DataRow("<ol><li>one<pre>code\n  indented</pre></li><li>two<blockquote>quote</blockquote></li><li><table><tr><th>h1</th><th>h2</th></tr><tr><td>1</td><td>2</td></tr></table></li></ol>",
        "1. one\n\n   ```\n   code\n     indented\n   ```\n2. two\n\n   > quote\n3. | h1 | h2 |\n   | --- | --- |\n   | 1 | 2 |")]
    [DataRow("<table><tr><th>a</th><th>b</th></tr><tr><td><h3>H</h3><blockquote>q</blockquote><pre>c d</pre><hr>x</td><td>text <div>div</div> more</td></tr></table>",
        "| a | b |\n| --- | --- |\n| H<br>q<br>`c d`<br>x | text<br>div<br>more |")]
    public void Converts_Tables(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    /// <summary>A table that is not a table of data is written as the blocks in its cells.</summary>
    [TestMethod]
    [DataRow("<table><tr><td>only</td></tr><tr><td>column</td></tr></table>", "only\n\ncolumn")]
    [DataRow("<table><tr><td><p>left block</p></td><td><div>right block</div></td></tr></table>", "left block\n\nright block")]
    [DataRow("<table role=\"presentation\"><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>", "a\n\nb\n\nc\n\nd")]
    [DataRow("<table><tr><td><table><tr><td>inner 1</td><td>inner 2</td></tr><tr><td>x</td><td>y</td></tr></table></td><td><h2>Side</h2><p>layout</p></td></tr></table>",
        "|  |  |\n| --- | --- |\n| inner 1 | inner 2 |\n| x | y |\n\n## Side\n\nlayout")]
    [DataRow("<td>cell outside</td><tr><td>row outside</td></tr>", "cell outside\n\nrow outside")]
    [DataRow("<table></table><table><tr></tr></table>x", "x")]
    public void Converts_LayoutTables(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    /// <summary>Text gets a backslash only where Markdown would read it as its own.</summary>
    [TestMethod]
    [DataRow("<p>snake_case and _private and 2 * 3 and *star* and [1] and [x](y) and a < b and AT&amp;T &amp;copy; `code` ~approx ~~strike~~</p>",
        "snake_case and \\_private and 2 * 3 and \\*star\\* and [1] and [x\\](y) and a < b and AT&T \\&copy; \\`code\\` ~approx \\~\\~strike\\~\\~")]
    [DataRow("<p>C:\\path\\to and a\\*b and foo_ and _foo_bar_ and __init__ and * and **bold** and a*b*c</p>",
        "C:\\path\\to and a\\\\\\*b and foo\\_ and \\_foo_bar\\_ and \\_\\_init\\_\\_ and * and \\*\\*bold\\*\\* and a\\*b\\*c")]
    [DataRow("<p>[link]: http://x</p>", "[link\\]: http://x")]
    [DataRow("<p>![img](x) and &lt;div&gt; and &amp;amp; and 1) one</p>", "![img\\](x) and \\<div> and \\&amp; and 1) one")]
    [DataRow("<p>&lt;b&gt; &#42;star&#42; a&nbsp;b &#95;x&#95; &lt;3 &lt;/p&gt;</p>", "\\<b> \\*star\\* a b \\_x\\_ <3 \\</p>")]
    [DataRow("<p>http://example.com/a_b_c?x=1&y=2</p>", "http://example.com/a_b_c?x=1&y=2")]
    // the start of a block
    [DataRow("<p># not heading</p><p>- not list</p><p>1. not ordered</p><p>> not quote</p><p>+ plus</p><p>---</p><p>2024. Year</p>",
        "\\# not heading\n\n\\- not list\n\n1\\. not ordered\n\n\\> not quote\n\n\\+ plus\n\n\\---\n\n2024\\. Year")]
    [DataRow("<p>-5</p><p>- 5</p><p>+1</p><p>#hashtag</p><p>####### seven</p><p>12) x</p><p>1234567890. ten digits</p>",
        "-5\n\n\\- 5\n\n+1\n\n#hashtag\n\n####### seven\n\n12\\) x\n\n1234567890. ten digits")]
    [DataRow("<p>===</p><p>a | b</p><p>---|---</p><p>~~~</p><p>a ~ b</p>", "\\===\n\na | b\n\n\\---|---\n\n\\~\\~\\~\n\na ~ b")]
    [DataRow("a<br>- b<br>===<br>1. c<br>| - | - |", "a  \n\\- b  \n\\===  \n1\\. c  \n\\| - | - |")]
    public void Converts_Escapes(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    [TestMethod]
    [DataRow("<html><head><title>T</title><meta charset=\"utf-8\"><style>p{}</style></head><body><select><option>opt</option></select><textarea>ta</textarea><svg><text>svg</text></svg><button>Button</button> <label>Label</label><template><p>t</p></template><noscript>ns</noscript><iframe>if</iframe>end</body></html>",
        "Button Labelend")]
    [DataRow("<div>One</div><script>var a = \"<p>no</p>\";</script><style>p > b {}</style><!-- <b>c</b> -->Two", "One\n\nTwo")]
    [DataRow("text <!-- c --> more</span> stray <span>unclosed", "text more stray unclosed")]
    [DataRow("<!DOCTYPE html><p>after doctype</p>", "after doctype")]
    [DataRow("<script>var first = 1;</script><style>p{}</style><title>T</title>the first tag of a page is not asked for", "the first tag of a page is not asked for")]
    public void Converts_OnlyWhatABrowserShows(string html, string expected)
    {
        Assert.AreEqual(expected, HtmlText.ToMarkdown(html));
    }

    // ----- the API -----

    [TestMethod]
    public void Markdown_OfATag_IsTheTagWithItsContent()
    {
        var doc = new HtmlParser("""
            <div id="d"><h2>Title</h2><p>Some <a href="/x">link</a> and <img src="i.png" alt="pic"></p>
            <ul><li>a</li><li>b</li></ul>
            <table><tr><th>k</th><th>v</th></tr><tr><td>1</td><td>2</td></tr></table><br></div>
            """);

        Assert.AreEqual("## Title", doc.Find(HtmlTag.H2).Markdown());
        Assert.AreEqual("[link](/x)", doc.Find(HtmlTag.A).Markdown());
        Assert.AreEqual("![pic](i.png)", doc.Find(HtmlTag.Img).Markdown());
        Assert.AreEqual("Some [link](/x) and ![pic](i.png)", doc.Find(HtmlTag.P).Markdown());
        Assert.AreEqual("- a\n- b", doc.Find(HtmlTag.Ul).Markdown());
        Assert.AreEqual("- b", doc.FindAll(HtmlTag.Li).Last().Markdown());
        Assert.AreEqual("| k | v |\n| --- | --- |\n| 1 | 2 |", doc.Find(HtmlTag.Table).Markdown());
        Assert.AreEqual("2", doc.FindAll(HtmlTag.Td).Last().Markdown());
        Assert.AreEqual("", doc.Find(HtmlTag.Br).Markdown());

        // the document is the sum of its parts
        Assert.AreEqual(doc.Find(HtmlTag.Div).Markdown(), doc.Markdown());
        StringAssert.StartsWith(doc.Markdown(), "## Title\n\nSome [link](/x)");
    }

    [TestMethod]
    public void Markdown_OfNothing()
    {
        Assert.IsNull(((ParserTag?)null).Markdown());
        Assert.IsNull(((HtmlParser?)null).Markdown());
        Assert.IsNull(HtmlText.ToMarkdown(null));
        Assert.IsNull(HtmlText.ToMarkdown(""));

        Assert.AreEqual("", HtmlText.ToMarkdown("<br/>"));
        Assert.AreEqual("", HtmlText.ToMarkdown(" \n "));
        Assert.AreEqual("", HtmlText.ToMarkdown("<!-- only a comment -->"));
        Assert.AreEqual("", new HtmlParser("").Markdown());
        Assert.AreEqual("", new HtmlParser("<p><!-- c --></p>").Find(HtmlTag.Comment).Markdown());
    }

    [TestMethod]
    public void Markdown_LinksAndImages_AreAbsolute_WhenThePageHasAUrl()
    {
        const string html = "<a href='/a?x=1&amp;y=2'>root</a> <a href='b.html'>relative</a> <img src='i.png'> <a href='https://other.example/x'>other</a> <a href='#top'>top</a> <a href='#'>none</a>";

        var doc = new HtmlParser(html, "https://example.com/dir/page.html");
        Assert.AreEqual(
            "[root](https://example.com/a?x=1&y=2) [relative](https://example.com/dir/b.html) ![](https://example.com/dir/i.png) "
            + "[other](https://other.example/x) [top](https://example.com/dir/page.html#top) none",
            doc.Markdown());

        // the same addresses as AttributeHref() / AttributeSrc()
        string markdown = doc.Markdown()!;
        foreach (var link in doc.FindAll(HtmlTag.A).Take(4))
        {
            StringAssert.Contains(markdown, $"]({link.AttributeHref()})");
        }
        StringAssert.Contains(markdown, $"![]({doc.Find(HtmlTag.Img).AttributeSrc()})");

        // without a page URL the addresses stay as written
        Assert.AreEqual("[root](/a?x=1&y=2) [relative](b.html) ![](i.png) [other](https://other.example/x) [top](#top) none", HtmlText.ToMarkdown(html));
    }

    /// <summary>
    /// A link that is a plain path is made absolute without System.Uri, any other with it, as AttributeHref() does:
    /// both ways have to give the same address for every link and every page URL.
    /// </summary>
    [TestMethod]
    public void Markdown_Links_AreTheAddressesOfAttributeHref()
    {
        string[] pages =
        [
            "https://example.com", "https://example.com/", "https://example.com/dir/page.html", "https://example.com/dir/",
            "https://example.com/a/b/c?q=1#frag", "http://EXAMPLE.com:8080/Dir/Page", "https://example.com/caf%C3%A9/my%20page.html",
            "https://example.com/dir/(x)/page", "https://user@example.com/a/", "file:///C:/dir/page.html", "about:blank", "not a url", "",
        ];
        string[] links =
        [
            "b.html", "/a/b", "a/b/c.html?x=1&y=2#top", "/", "/a/", "a", "a.b.c/d-e_f~g", "/search?q=a+b,c;d", "x#f", "/a?b#c",
            "page-2.html", "catalogue/category/books_1/index.html", "media/cache/2c/da/2cdad67c44b002e7ead0cc35693c0e8b.jpg",
            // not plain paths: these are resolved by System.Uri
            "../up.html", "./here.html", "a/../b", "a/./b", "/a/.hidden", "a//b", "//other.example/x", "?q=1", "#top", "a.", "a./b", "a?", "a#",
            "a%20b", "a b", "a:b", "mailto:x@y.z", "tel:+1-234", "HTTPS://Other.example/X", "ftp://f.example/a", "/a\\b", "é", "a(b)c", "a.?b", "a.#b",
            "/a/b/", "A/B", "0", "~user/x", "a=b", "a&b", "-a", "_a", ".a", "..a",
        ];

        foreach (string page in pages)
        {
            var html = new System.Text.StringBuilder();
            foreach (string link in links) html.Append($"<a href=\"{link}\">x</a> <img src=\"{link}\">\n");

            var doc = new HtmlParser(html.ToString(), page);
            string[] lines = doc.Markdown()!.Split(' ', '\n');
            var anchors = doc.FindAll(HtmlTag.A).ToList();
            var images = doc.FindAll(HtmlTag.Img).ToList();
            Assert.HasCount(2 * links.Length, lines, page);

            for (int i = 0; i < links.Length; i++)
            {
                // an address is written with "%20" for a space and without unpaired parentheses; none of these links has them resolved
                string href = anchors[i].AttributeHref()!.Replace(" ", "%20").Replace("\\", "%5C");
                string src = images[i].AttributeSrc()!.Replace(" ", "%20").Replace("\\", "%5C");

                Assert.AreEqual($"[x]({href})", lines[2 * i], $"{links[i]} on {page}");
                Assert.AreEqual($"![]({src})", lines[2 * i + 1], $"{links[i]} on {page}");
            }
        }
    }

    [TestMethod]
    public void Markdown_Options()
    {
        var doc = new HtmlParser("<p>See <a href=\"/a\" title=\"t\">the <b>link</b></a> and <img src=\"i.png\" alt=\"pic\">.</p><a href=\"/c\"><img src=\"c.png\"></a>");

        Assert.AreEqual("See [the **link**](/a \"t\") and ![pic](i.png).\n\n[![](c.png)](/c)", doc.Markdown());
        Assert.AreEqual("See the **link** and ![pic](i.png).\n\n![](c.png)", doc.Markdown(new MarkdownOptions { Links = false }));
        Assert.AreEqual("See [the **link**](/a \"t\") and .", doc.Markdown(new MarkdownOptions { Images = false }));
        Assert.AreEqual("See the **link** and .", doc.Markdown(new MarkdownOptions { Links = false, Images = false }));
        Assert.AreEqual("See the **link** and .", doc.Find(HtmlTag.P).Markdown(new MarkdownOptions { Links = false, Images = false }));
    }

    [TestMethod]
    public void Markdown_OfTheHead_IsEmpty_ButATitleAskedForGivesItsText()
    {
        var doc = new HtmlParser("<html><head><title>The &amp; title</title><script>var x;</script></head><body><p>body</p></body></html>");

        Assert.AreEqual("body", doc.Markdown());
        Assert.AreEqual("", doc.Find(HtmlTag.Head).Markdown());
        Assert.AreEqual("The & title", doc.Find(HtmlTag.Title).Markdown());
    }

    [TestMethod]
    public void Markdown_OfADocumentParsedFromMemory_AndAfterDispose()
    {
        char[] buffer = "junk<h1>Title</h1><p>text &amp; more</p>junk".ToCharArray();
        var doc = HtmlParser.Parse(buffer.AsMemory(4, buffer.Length - 8));

        Assert.AreEqual("# Title\n\ntext & more", doc.Markdown());

        var heading = doc.Find(HtmlTag.H1);
        doc.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => doc.Markdown());
        Assert.ThrowsExactly<ObjectDisposedException>(() => heading.Markdown());
    }

    [TestMethod]
    public void Markdown_MakesNoObjectPerTag()
    {
        // 2000 tags and no links: the result and little else
        string html = string.Concat(Enumerable.Repeat("<div class=\"row\"><b>bold</b> <i>text</i> &amp; <span>more</span></div>\n", 400));
        var doc = new HtmlParser(html);
        string expected = doc.Markdown()!; // rents the buffers

        long before = GC.GetAllocatedBytesForCurrentThread();
        string markdown = doc.Markdown()!;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(expected, markdown);
        long result = 2L * markdown.Length + 64;
        Assert.IsLessThanOrEqualTo(result + 1024, allocated, $"allocated {allocated} bytes for a result of {result}");
    }
}
