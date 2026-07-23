using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Website.Test.ScheduledJobs
{
    /// <summary>
    /// One generated product: enough to (a) create a real Commerce product/variation and
    /// (b) know what slug should resolve to it afterwards. CMS-neutral on purpose — the
    /// Commerce-specific job turns this into catalog content; the scale tests turn it into
    /// <c>SlugEntry</c> values directly.
    /// </summary>
    public sealed class ProductSpec
    {
        public ProductSpec(string displayName, string slug, string sku, decimal price,
            string culture, IReadOnlyDictionary<string, string> data)
        {
            DisplayName = displayName;
            Slug = slug;
            Sku = sku;
            Price = price;
            Culture = culture;
            Data = data;
        }

        public string DisplayName { get; }
        public string Slug { get; }
        public string Sku { get; }
        public decimal Price { get; }
        public string Culture { get; }
        public IReadOnlyDictionary<string, string> Data { get; }
    }

    /// <summary>Knobs for <see cref="RandomCatalogData"/>. Defaults give a realistic-ish catalog.</summary>
    public sealed class RandomCatalogOptions
    {
        /// <summary>How many distinct products (content items) to generate.</summary>
        public int ProductCount { get; set; } = 10_000;

        /// <summary>Cultures each product gets a slug in. One ProductSpec is emitted per culture.</summary>
        public string[] Cultures { get; set; } = { "en" };

        /// <summary>Fraction (0..1) of products that get a second alias slug sharing the same content id.</summary>
        public double AliasFraction { get; set; } = 0.05;

        /// <summary>
        /// Fraction (0..1) of products forced to collide on an already-used (slug, culture) with a
        /// different content id — exercises last-write-wins. Keep small.
        /// </summary>
        public double CollisionFraction { get; set; } = 0.0;

        /// <summary>Fraction (0..1) of slugs that are multi-segment paths (category/sub/slug).</summary>
        public double DeepPathFraction { get; set; } = 0.15;

        /// <summary>Number of string entries placed in each ProductSpec's Data dictionary.</summary>
        public int DataFieldCount { get; set; } = 3;
    }

    /// <summary>
    /// Deterministic, seeded generator of fake catalog products. Same seed + options =&gt; identical
    /// stream, so every store and every run sees the same data. Streams lazily — never materializes
    /// the whole catalog (matters at 1M, and for the low-memory goal).
    /// </summary>
    public sealed class RandomCatalogData
    {
        private static readonly string[] Adjectives =
        {
            "blue","red","rugged","compact","deluxe","premium","classic","modern","vintage","eco",
            "smart","ultra","mini","mega","pro","lite","heavy","light","silent","rapid",
            "arctic","desert","urban","alpine","coastal","matte","glossy","brushed","forged","woven"
        };

        private static readonly string[] Nouns =
        {
            "widget","gadget","sprocket","bottle","jacket","sneaker","lamp","chair","mug","backpack",
            "headset","keyboard","monitor","wallet","watch","drone","speaker","camera","tripod","blender",
            "kettle","toaster","helmet","glove","scarf","umbrella","notebook","pen","charger","router"
        };

        private static readonly string[] Categories =
        {
            "electronics","apparel","home","outdoors","kitchen","office","sports","toys"
        };

        private readonly int _seed;
        private readonly RandomCatalogOptions _options;

        public RandomCatalogData(int seed, RandomCatalogOptions? options = null)
        {
            _seed = seed;
            _options = options ?? new RandomCatalogOptions();
        }

        /// <summary>
        /// Stream the products. <paramref name="contentIdFactory"/> turns a 1-based product index into
        /// the neutral content id string (the Commerce job passes the created content's id; the scale
        /// tests pass e.g. <c>i =&gt; i + "|"</c> to mimic the Optimizely wire format).
        /// </summary>
        public IEnumerable<ProductSpec> Stream(Func<int, string> contentIdFactory)
        {
            if (contentIdFactory is null) throw new ArgumentNullException(nameof(contentIdFactory));

            // Per-stream RNG so the sequence is reproducible and independent of caller iteration timing.
            var rng = new Random(_seed);
            var usedKeys = new HashSet<string>(StringComparer.Ordinal); // for collision injection
            var cultures = _options.Cultures.Length > 0 ? _options.Cultures : new[] { "en" };

            for (var i = 1; i <= _options.ProductCount; i++)
            {
                var contentId = contentIdFactory(i);
                var displayName = BuildName(rng, i);
                var baseSlug = BuildSlug(rng, displayName);
                var sku = $"SKU-{i:D8}";
                var price = Math.Round((decimal)(rng.NextDouble() * 990 + 10), 2);
                var data = BuildData(displayName, sku, price);

                foreach (var culture in cultures)
                {
                    yield return new ProductSpec(displayName, baseSlug, sku, price, culture, data);

                    // Optional second alias slug pointing at the same content id.
                    if (rng.NextDouble() < _options.AliasFraction)
                    {
                        var alias = baseSlug + "-" + sku.ToLowerInvariant();
                        yield return new ProductSpec(displayName, alias, sku, price, culture, data);
                    }
                }
                usedKeys.Add(baseSlug);
            }
        }

        private string BuildName(Random rng, int index)
        {
            var a = Adjectives[rng.Next(Adjectives.Length)];
            var n = Nouns[rng.Next(Nouns.Length)];
            var model = (char)('A' + rng.Next(26));
            // The index is unique per product, so the name (and therefore its slug) is unique
            // without any extra suffix — e.g. "Arctic Sneaker W699".
            return $"{Capitalize(a)} {Capitalize(n)} {model}{index}";
        }

        private string BuildSlug(Random rng, string displayName)
        {
            var slug = Slugify(displayName); // name already carries the unique model number
            if (rng.NextDouble() < _options.DeepPathFraction)
            {
                var cat = Categories[rng.Next(Categories.Length)];
                var sub = Nouns[rng.Next(Nouns.Length)];
                slug = $"{cat}/{sub}/{slug}";
            }
            return slug;
        }

        private IReadOnlyDictionary<string, string> BuildData(string displayName, string sku, decimal price)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_options.DataFieldCount >= 1) d["title"] = displayName;
            if (_options.DataFieldCount >= 2) d["sku"] = sku;
            if (_options.DataFieldCount >= 3) d["price"] = price.ToString("0.00");
            for (var k = 4; k <= _options.DataFieldCount; k++)
                d["field" + k] = "v" + k;
            return d;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Lower-case, spaces/non-alphanumerics to single hyphens. (SlugKey re-normalizes on store.)</summary>
        public static string Slugify(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(value.Length);
            var lastHyphen = false;
            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastHyphen = false;
                }
                else if (!lastHyphen)
                {
                    sb.Append('-');
                    lastHyphen = true;
                }
            }
            return sb.ToString().Trim('-');
        }
    }
}
