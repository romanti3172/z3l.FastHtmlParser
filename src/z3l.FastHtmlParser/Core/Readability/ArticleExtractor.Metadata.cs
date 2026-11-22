using System.Text.Json;
using System.Text.RegularExpressions;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Core.Readability;

/// <summary>What the page says about its article: JSON-LD first, then the meta tags.</summary>
internal struct ArticleMetadata
{
    public string? Title, Byline, Excerpt, SiteName, PublishedTime, Image;
}

internal sealed partial class ArticleExtractor
{
    private ArticleMetadata ReadMetadata()
    {
        var jsonLd = ReadJsonLd();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var source = _document.Source;

        for (int i = 0; i < _count; i++)
        {
            ref readonly var node = ref _data.Node(i);
            if (node.Key != (int)HtmlTag.Meta) continue;

            string? name = null, property = null, content = null;
            int openEnd = node.InnerStart > 0 && node.InnerEnd > 0 ? node.InnerStart - 1 : node.End;
            var scanner = new AttributeScanner(source, node.Start, openEnd);
            while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
            {
                if (valueStart < 0) continue;
                var attribute = source.Slice(nameStart, nameLength);
                var value = source.Slice(valueStart, valueLength);
                if (attribute.Equals("name", StringComparison.OrdinalIgnoreCase)) name ??= Decode(value);
                else if (attribute.Equals("property", StringComparison.OrdinalIgnoreCase)) property ??= Decode(value);
                else if (attribute.Equals("content", StringComparison.OrdinalIgnoreCase)) content ??= Decode(value);
            }
            if (string.IsNullOrWhiteSpace(content)) continue;
            content = content.Trim();

            bool matched = false;
            if (property != null)
            {
                foreach (Match match in MetaProperty().Matches(property))
                {
                    var key = Spaces().Replace(match.Value, "").ToLowerInvariant();
                    values[key] = content; // the last one counts, as in Readability
                    matched = true;
                }
            }
            if (!matched && name != null && MetaName().IsMatch(name))
            {
                var key = Spaces().Replace(name, "").ToLowerInvariant().Replace('.', ':');
                values[key] = content;
            }
        }

        string? Get(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        var metadata = new ArticleMetadata
        {
            Title = jsonLd.Title ?? Get("dc:title", "dcterm:title", "og:title", "weibo:article:title", "weibo:webpage:title", "title", "twitter:title", "parsely-title"),
            Byline = jsonLd.Byline ?? Get("dc:creator", "dcterm:creator", "author", "parsely-author"),
            Excerpt = jsonLd.Excerpt ?? Get("dc:description", "dcterm:description", "og:description", "weibo:article:description", "weibo:webpage:description", "description", "twitter:description"),
            SiteName = jsonLd.SiteName ?? Get("og:site_name"),
            PublishedTime = jsonLd.PublishedTime ?? Get("article:published_time", "parsely-pub-date", "dc:date", "dcterm:date"),
            Image = jsonLd.Image ?? Get("og:image", "twitter:image", "twitter:image:src", "og:image:url", "og:image:secure_url"),
        };

        // article:author is often the address of the author's page, which is no name
        if (metadata.Byline == null && Get("article:author") is { } author && !author.Contains("://", StringComparison.Ordinal))
        {
            metadata.Byline = author;
        }

        metadata.Title = Tidy(metadata.Title);
        metadata.Byline = Tidy(metadata.Byline);
        metadata.Excerpt = Tidy(metadata.Excerpt);
        metadata.SiteName = Tidy(metadata.SiteName);
        metadata.PublishedTime = Tidy(metadata.PublishedTime);
        metadata.Image = metadata.Image == null ? null : Resolve(metadata.Image.Trim());
        return metadata;

        static string? Tidy(string? value)
        {
            if (value == null) return null;
            // the values may hold entities written twice ("&amp;amp;") and line breaks
            value = Spaces().Replace(HtmlDecodeCore.HtmlDecode(value), " ").Trim();
            return value.Length == 0 ? null : value;
        }
    }

    /// <summary>A URL from the metadata made absolute against the page, when the page has an address.</summary>
    private string Resolve(string url)
    {
        if (_document.PageUri is not { } baseUri || Uri.TryCreate(url, UriKind.Absolute, out var absolute) && !absolute.IsFile)
        {
            return url;
        }
        return Uri.TryCreate(baseUri, url, out var combined) ? combined.ToString() : url;
    }

    // ----- JSON-LD -----

    private ArticleMetadata ReadJsonLd()
    {
        var source = _document.Source;
        for (int i = 0; i < _count; i++)
        {
            ref readonly var node = ref _data.Node(i);
            if (node.Key != (int)HtmlTag.Script || node.InnerStart <= 0 || node.InnerEnd < node.InnerStart) continue;
            if (!string.Equals(_data[i].AttributeByName("type").Trim(), "application/ld+json", StringComparison.OrdinalIgnoreCase)) continue;

            var json = source.Slice(node.InnerStart, node.InnerEnd - node.InnerStart + 1).Trim();
            if (json.StartsWith("<![CDATA[")) json = json.Slice(9);
            if (json.EndsWith("]]>")) json = json.Slice(0, json.Length - 3);

            try
            {
                using var document = JsonDocument.Parse(json.ToString(), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 64,
                });
                if (FindArticle(document.RootElement, depth: 0) is { } article)
                {
                    return ReadArticle(article);
                }
            }
            catch (JsonException)
            {
                // a broken block: the next one, or the meta tags
            }
        }
        return default;
    }

    /// <summary>An object of an article type: the root, an item of a root array, or an item of "@graph".</summary>
    private static JsonElement? FindArticle(JsonElement element, int depth)
    {
        if (depth > 3) return null;

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (FindArticle(item, depth + 1) is { } found) return found;
            }
            return null;
        }
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (IsArticleType(element)) return element;

        if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in graph.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && IsArticleType(item)) return item;
            }
        }
        return null;
    }

    private static bool IsArticleType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type)) return false;

        if (type.ValueKind == JsonValueKind.String) return IsArticleType(type.GetString());
        if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && IsArticleType(item.GetString())) return true;
            }
        }
        return false;
    }

    private static bool IsArticleType(string? type)
    {
        if (string.IsNullOrEmpty(type)) return false;
        int slash = type.LastIndexOf('/');
        if (slash >= 0) type = type.Substring(slash + 1); // "http://schema.org/NewsArticle"
        return ArticleTypes.Contains(type);
    }

    private static readonly HashSet<string> ArticleTypes = new(StringComparer.Ordinal)
    {
        "Article", "AdvertiserContentArticle", "NewsArticle", "AnalysisNewsArticle", "AskPublicNewsArticle",
        "BackgroundNewsArticle", "OpinionNewsArticle", "ReportageNewsArticle", "ReviewNewsArticle", "Report",
        "SatiricalArticle", "ScholarlyArticle", "MedicalScholarlyArticle", "SocialMediaPosting", "BlogPosting",
        "LiveBlogPosting", "DiscussionForumPosting", "TechArticle", "APIReference", "Recipe", "HowTo",
    };

    private ArticleMetadata ReadArticle(JsonElement article)
    {
        var metadata = new ArticleMetadata();

        string? name = String(article, "name");
        string? headline = String(article, "headline");
        if (name != null && headline != null && name != headline)
        {
            // both: the name, unless only the headline is like the title of the page
            string? pageTitle = ArticleTitle();
            metadata.Title = pageTitle != null && TextSimilarity(headline, pageTitle) > 0.75 && TextSimilarity(name, pageTitle) <= 0.75
                ? headline
                : name;
        }
        else
        {
            metadata.Title = headline ?? name;
        }

        if (article.TryGetProperty("author", out var author))
        {
            metadata.Byline = Names(author);
        }

        metadata.Excerpt = String(article, "description");
        if (article.TryGetProperty("publisher", out var publisher) && publisher.ValueKind == JsonValueKind.Object)
        {
            metadata.SiteName = String(publisher, "name");
        }
        metadata.PublishedTime = String(article, "datePublished");
        if (article.TryGetProperty("image", out var image)) metadata.Image = ImageUrl(image);
        return metadata;
    }

    private static string? String(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    /// <summary>"name" of an object, the names of an array of them joined with ", ", or a plain string.</summary>
    private static string? Names(JsonElement author)
    {
        switch (author.ValueKind)
        {
            case JsonValueKind.String:
                return author.GetString();
            case JsonValueKind.Object:
                return String(author, "name");
            case JsonValueKind.Array:
                var names = new List<string>();
                foreach (var item in author.EnumerateArray())
                {
                    if (Names(item) is { Length: > 0 } one) names.Add(one.Trim());
                }
                return names.Count > 0 ? string.Join(", ", names) : null;
            default:
                return null;
        }
    }

    private static string? ImageUrl(JsonElement image)
    {
        switch (image.ValueKind)
        {
            case JsonValueKind.String:
                return image.GetString();
            case JsonValueKind.Object:
                return String(image, "url") ?? String(image, "contentUrl");
            case JsonValueKind.Array:
                foreach (var item in image.EnumerateArray())
                {
                    if (ImageUrl(item) is { Length: > 0 } url) return url;
                }
                return null;
            default:
                return null;
        }
    }

    // ----- the title -----

    private string? PageTitle()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_data.Node(i).Key == (int)HtmlTag.Title)
            {
                var text = _data[i].Text();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// Readability's _getArticleTitle: the &lt;title&gt; without the name of the site ("Title | Site",
    /// "Site: Title"), or the only &lt;h1&gt; when the title is too short or too long.
    /// </summary>
    private string? ArticleTitle()
    {
        string? original = PageTitle();
        if (original == null) return Heading();

        string title = original;
        bool hierarchical = false;

        if (Separator().IsMatch(title))
        {
            hierarchical = Hierarchical().IsMatch(title);
            // before the last separator; if that is too short, after the first one
            title = BeforeLastSeparator().Replace(original, "$1");
            if (WordCount(title) < 3) title = AfterFirstSeparator().Replace(original, "$1");
        }
        else if (title.Contains(": ", StringComparison.Ordinal))
        {
            bool heading = false;
            for (int i = 0; i < _count && !heading; i++)
            {
                if (_data.Node(i).Key is (int)HtmlTag.H1 or (int)HtmlTag.H2 && _data[i].Text()?.Trim() == title.Trim()) heading = true;
            }
            if (!heading)
            {
                title = original.Substring(original.LastIndexOf(':') + 1);
                if (WordCount(title) < 3)
                {
                    title = original.Substring(original.IndexOf(':') + 1);
                }
                else if (WordCount(original.Substring(0, original.IndexOf(':'))) > 5)
                {
                    title = original;
                }
            }
        }
        else if (title.Length > 150 || title.Length < 15)
        {
            if (Heading() is { } h1) title = h1;
        }

        title = Spaces().Replace(title.Trim(), " ");
        int words = WordCount(title);
        if (words <= 4 && (!hierarchical || words != WordCount(SeparatorChars().Replace(original, "")) - 1))
        {
            title = original;
        }
        return title;
    }

    /// <summary>The text of the only &lt;h1&gt; of the page; null when it has none or more than one.</summary>
    private string? Heading()
    {
        string? found = null;
        for (int i = 0; i < _count; i++)
        {
            if (_data.Node(i).Key != (int)HtmlTag.H1) continue;
            if (found != null) return null;
            found = _data[i].Text() ?? string.Empty;
        }
        return string.IsNullOrWhiteSpace(found) ? null : found.Trim();
    }

    private static int WordCount(string text) => Spaces().Split(text.Trim()).Count(w => w.Length > 0);

    [GeneratedRegex(@"\s*(article|dc|dcterm|og|twitter)\s*:\s*(author|creator|description|published_time|title|site_name|image|image:src|image:url|image:secure_url)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaProperty();

    [GeneratedRegex(@"^\s*(?:(dc|dcterm|og|twitter|parsely|weibo:(article|webpage))\s*[-\.:]\s*)?(author|creator|pub-date|description|title|site_name|date|image|image:src)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaName();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@" [\|\-–—\\\/>»] ", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();

    [GeneratedRegex(@" [\\\/>»] ", RegexOptions.CultureInvariant)]
    private static partial Regex Hierarchical();

    [GeneratedRegex(@"(.*)[\|\-–—\\\/>»] .*", RegexOptions.CultureInvariant)]
    private static partial Regex BeforeLastSeparator();

    [GeneratedRegex(@"[^\|\-–—\\\/>»]*[\|\-–—\\\/>»](.*)", RegexOptions.CultureInvariant)]
    private static partial Regex AfterFirstSeparator();

    [GeneratedRegex(@"[\|\-–—\\\/>»]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorChars();
}
