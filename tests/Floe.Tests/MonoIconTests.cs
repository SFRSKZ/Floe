using Floe.Core.Services.MonoIcons;

namespace Floe.Tests;

public sealed class MonoIconTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "floe-mono-" + Guid.NewGuid().ToString("N")[..8]);

    public MonoIconTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temp */ }
    }

    private MonoIconBank Bank(string json)
    {
        File.WriteAllText(Path.Combine(_root, "index.json"), json);
        Directory.CreateDirectory(Path.Combine(_root, "png"));
        File.WriteAllBytes(Path.Combine(_root, "png", "spotify.png"), new byte[] { 1 });
        return MonoIconBank.Load(_root);
    }

    [Fact]
    public void Plain_ignores_case_spaces_and_punctuation()
    {
        Assert.Equal("blender50", MonoIconNames.Plain("Blender 5.0"));
        Assert.Equal("komutistemi", MonoIconNames.Plain("Komut İstemi".Replace("İ", "I")).Replace("ı", "i"));
        Assert.Equal(string.Empty, MonoIconNames.Plain(null));
    }

    [Fact]
    public void Bank_matches_the_same_name_only()
    {
        var bank = Bank("""[{"name":"Spotify","slug":"spotify"},{"name":"Steam","slug":"steam"}]""");
        Assert.Equal("spotify", bank.Find(new[] { "spotify" }));
        Assert.Null(bank.Find(new[] { "Spotify Premium" }));
        Assert.Null(bank.Find(new string?[] { null, "", "Unknown" }));
    }

    [Fact]
    public void Bank_takes_the_first_candidate_that_matches()
    {
        var bank = Bank("""[{"name":"Spotify","slug":"spotify"},{"name":"Steam","slug":"steam"}]""");
        Assert.Equal("steam", bank.Find(new[] { "Nothing", "STEAM", "Spotify" }));
    }

    [Fact]
    public void Bank_refuses_a_name_two_entries_share()
    {
        var bank = Bank("""[{"name":"Foo Bar","slug":"a"},{"name":"foo-bar","slug":"b"}]""");
        Assert.Null(bank.Find(new[] { "Foo Bar" }));
    }

    [Fact]
    public void Bank_knows_the_english_names_of_its_turkish_entries()
    {
        var bank = Bank("""[{"name":"Dosya Gezgini","slug":"dosya-gezgini"}]""");
        Assert.Equal("dosya-gezgini", bank.Find(new[] { "File Explorer" }));
    }

    [Fact]
    public void Missing_or_broken_bank_is_empty()
    {
        Assert.Equal(0, MonoIconBank.Load(Path.Combine(_root, "nope")).Count);
        File.WriteAllText(Path.Combine(_root, "index.json"), "{{{");
        Assert.Null(MonoIconBank.Load(_root).Find(new[] { "Spotify" }));
    }

    [Fact]
    public void Quality_rejects_empty_solid_and_shattered_icons()
    {
        Assert.Equal("boş", MonoIconQuality.Check(new byte[256 * 256], 256, 256));

        var solid = Enumerable.Repeat((byte)255, 256 * 256).ToArray();
        Assert.Equal("dolu dikdörtgen", MonoIconQuality.Check(solid, 256, 256));

        var random = new Random(3);
        var speckle = new byte[512 * 512];
        for (var i = 0; i < speckle.Length; i++) speckle[i] = random.NextDouble() < 0.35 ? (byte)255 : (byte)0;
        Assert.Equal("parçalanmış", MonoIconQuality.Check(speckle, 512, 512));
    }

    [Fact]
    public void Quality_rejects_a_filled_disc_but_not_a_ring()
    {
        var disc = new byte[256 * 256];
        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                if (((x - 128) * (x - 128)) + ((y - 128) * (y - 128)) < 100 * 100) disc[(y * 256) + x] = 255;
            }
        }

        Assert.Equal("dolu daire", MonoIconQuality.Check(disc, 256, 256));
    }

    [Fact]
    public void Quality_accepts_a_ring_and_a_small_block()
    {
        var ring = new byte[256 * 256];
        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                var d = Math.Sqrt(((x - 128) * (x - 128)) + ((y - 128) * (y - 128)));
                if (d is > 70 and < 100) ring[(y * 256) + x] = 255;
            }
        }

        Assert.Null(MonoIconQuality.Check(ring, 256, 256));
        Assert.NotNull(MonoIconQuality.Check(new byte[10], 256, 256));
    }

    [Fact]
    public void Cache_record_is_current_only_for_the_same_stamp_and_file()
    {
        var cache = new MonoIconCache(Path.Combine(_root, "cache"));
        var key = MonoIconCache.KeyOf(@" C:\Apps\Çok Özel.exe ");
        var file = MonoIconCache.FileFor(key);
        Directory.CreateDirectory(Path.Combine(_root, "cache"));
        File.WriteAllBytes(cache.PathOf(file), new byte[] { 1 });
        var stamp = MonoIconCache.StampOf("src:a|1");
        cache.Put(key, new MonoIconCache.Record { Stamp = stamp, File = file, Kind = "converted" });

        Assert.NotNull(cache.Current(key, stamp));
        Assert.Null(cache.Current(key, MonoIconCache.StampOf("src:a|2")));

        File.Delete(cache.PathOf(file));
        Assert.Null(cache.Current(key, stamp));
    }

    [Fact]
    public void Cache_survives_a_restart_and_a_broken_index()
    {
        var folder = Path.Combine(_root, "cache");
        var first = new MonoIconCache(folder);
        first.Put("k", new MonoIconCache.Record { Stamp = "s", Kind = "own", Note = "kaynak yok" });
        Assert.Equal("kaynak yok", new MonoIconCache(folder).Known("k")?.Note);

        File.WriteAllText(Path.Combine(folder, "index.json"), "not json");
        Assert.Null(new MonoIconCache(folder).Known("k"));
    }
}
