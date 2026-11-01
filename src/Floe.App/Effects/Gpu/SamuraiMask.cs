using System.Text;

namespace Floe.App.Effects.Gpu;

/// <summary>The Samurai mask as cells, for shaders that draw it point by point: r is red, g is chrome.</summary>
internal static class SamuraiMask
{
    public const int Columns = 110, Lines = 120;
    private const int WordsPerLine = 7;

    private static readonly string[] Rows =
    {
        "..............................................................................................................",
        "..............................................................................................................",
        "..............................................................................................................",
        ".........................................................................r....................................",
        ".........................................................................r....................................",
        "........................................................................rr....................................",
        "......................................................................rrrr.......................r............",
        "....................................................................rrrrr........................r............",
        "..................................................................rrrrrr.........................r............",
        ".................................................................rrrrrr.........................rr............",
        "...............................................................rrrrrr...........................rr............",
        "..............................................................rrrrrr................r..........rrr............",
        "..............................................................rrrrr.................r.........rrrr............",
        ".............................................................rrrrrr................rr........rrrr.............",
        ".............r............................r..................rrrrr.................rr........rrr..............",
        ".............r...........................r..................rrrrrr...r.............rr.......rrr...............",
        ".............r...........................r..................rrrrr....r............rrr.......rr................",
        "............rr.......................r..r...................rrrrr....r...........rrrr.......rr................",
        "............rr.......................r..r...................rrrrrr...r...........rrr........r.................",
        "...........rrr......................r...r............r......rrrrrr..rr..........rrr.........r.................",
        "...........rrr......................r...r...........r.......rrrrrrrrrr..........rr............................",
        "...........rrr......................r..............rr.......rrrrrrrrrr..........r.............................",
        "...........rrr.....................rr..............rr......rrrrrrrrrrr.....r................................r.",
        "............rr.....................rr..............rr......rrrrrrrrrrr....rr...............................rr.",
        "............rr.....................rrr.............rrr.....rrrrrrrrrrrr..rrr...........r..................rr..",
        "............rr.....................rrrr.............rr.....rrrrrrrrrrrrrrrrr.......r...r..................rr..",
        ".............r.....................rrrr.............rr....rrrrrrrrrr.rrrrrrr......r...rr.................rr...",
        ".............r......................rrrr......r....rrrr..rrrrrrrrrrrrrrrrrr......r....r..................rr...",
        "..............r.....................rrrrr......r..rrrrrrrrrrrrrrrrrrrrr..rr.....rr....r..................r....",
        "....................................rrrrr......rrrrrrrrrrrrrrrrrrrrrrr...r......rr...rr............r.....r....",
        "...........................r.........rrrr......rrrrrrrrrrrrrrrrrrrrrr....r.....rrr...r...rr........r..........",
        "...........................rr........rrrrr.....rrrrrrrrrrrrrrrrrrrrrr.r.r......rrrr..r...rr.......r...........",
        "............................rr........rrrr....rrrrrrrrrrrrrrrrrrrrrr..r.......rrrrr..r...rr.......r...........",
        "............................rr.........rrr....rrrrrrrrrrrrrrrrrrrrrr.rr.......rrrrr......rrr......r...........",
        "..................r..........rr........rrr....rrrrrrrrrrrrrrrrrrrrrr.rr......rrrrrr.....rrrr......rr..........",
        "...................rr.........r........rrr....rrrrrrrrrrrrrrrrrrrrrrrrr.....rrrrrrr....rrrrr......rr..........",
        "...................rrr.............r...rr..r..rrrrrrrrrrrrrrrrrrrrrrrrrr..rrrrrrrrr....rrrrr......rr..........",
        "....................rrr.............r.rrr..r..rrrrrrrrrrrrrrrrrrrr.rrrrrrrrrrrrrrrr....rrrr.......rr..........",
        "....................rrrr..............rrr.r..rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr....rrrrr.......rrr.........",
        "................r....rrrr.............rrrrr..rrrrrrrrrrrrrrrrrrrrrr.rrrrrrrrrrrrrr....rrrr........rrr.........",
        "....r.......r...r.....rrr............rrrrrr.rrrrrrrrrrrrrrrrrrrrrr.rrrrrrrrrrrrrrr....rr....r......rr.........",
        "....r........r..r.....rrrr...r....r..rrrrrrrrrrrrrrrrrrrr.rrrrrr...rrrrrrrrr.rrrrr....r.....r......rr.........",
        "....rr.......r..rr.....rrr........r..rrrrrrrrrrrrrrrrrrr..rrrr...r.rrrrrrrr..rrrrr....r....rr......rr.........",
        "....rr.......rr..rr....rrr....r..rrr.rrrrrrrrrrrrrrrrrr...rr...rrr.rrrrrrrr..rrrrrr..r.....rr......r..........",
        ".....rr......rrr.rrr...rrrr...r..rrr.rrrrrrrrrrrrrrrrr..r.r..rrrrr.rrrrrrr....rrrrr.rr.....rr......r..........",
        ".....rr......rrrr.rr...rrrr...rr.rrr..rrrrrrrrrrrrrrr.rrr.r.rrrrrr.rrrrrrr....rrrrr.rr.....rrr....r...........",
        "......rr.....rrrrrrrr...rrr...rrrrrrr.rrrrr.rrrrrrrr.rrrr..rrrrrr.rrrrrrr..r..rrrrr.rr..r..rrr....r...........",
        ".......r.....rrrrrr.r...rrr....rrrrrrr.rrrr..rr.rrr..rrrrr.rrrrrr.rrrrrrrrrrrrrrrrr.rrr..r.rrr................",
        ".......r......rrrrr..r..rrr....rrrrrrr.rrrr...r..rr.rrrrrrrrrrrrr.rrrrrrrrrrrrrrrrr..rr..rrrrr................",
        "..............rrrrr......rr....rrrrrrrr.rrr......rr.rrrrrrrrrrrr.rrrrrrrrrrrrrrrrrr..rr..rrrrr................",
        "................rrr......rr.....rrrrrrrrrrrr....r.rrrrrrrrrrrrrr.rr..rrrrrrrrrrrrrrr.rrr..rrrr................",
        ".................r........r.ggg.rrrrrrrrrrrrr...rrrrrrrrrrrrrrr.rr..rrrrrrrrrrrrrrrr.rr...rrr.................",
        "....................ggg.g.rgggg.rrrrrrrrrrrrrr..rrrrrrrrrrrrrrr.r..rrrrrrrrrrrrrrrrr.rr...rrr.................",
        "...........r........gggggg.gggg..rrrrrrrrrrrrrr.rrrrrrrrrrrrrr....rrrrrrrrrrrrr.rrrr.rr..rrr..................",
        ".r..........r......ggggggg.ggggg.rrrrrrrrrrrrrr.rrrrrrrrrrrrrr...rrrrrrrrrrr......rr..r..rrr..................",
        ".r..........r.....gggggggggggggg.rrr.rrr.rrrrrrrrrrrrrrrrrrrrr..rrrrrrrrrrr........rr.r..rr..r................",
        "..r.........r....gggg...gggggggg.rrr.rrr.rrrrrrrrrrrrrr.rrrrrr..rrrrrrrrrr.........rr.r.rrr.rr................",
        "............r....gg..........ggg.rrr.rrr.rrrrrrrrrr.rrr.rrrrrr.rrrrrrrrrr..........rr.r.rr..rr................",
        "............rrr.............g....rrr.rrrrrrrrrrrrrr.rr..rrrrrr..rrrrrrrrr..........rrr..rr.rrr................",
        "...........rrrr...........ggg..g..rr.rrrrrrrrrrrrrr.rr..rrrrrrrrrrrrrrrr...........rrr..rr.rrr................",
        "...........rrrr.....ggg..gggg......r..rrrrrrrrrrrrr.rr..rrrrrrrrrrrrrrr............rrr..rr.rrr................",
        "...........rrrrr..g..ggg.gg.ggg....r..rrrrrrrrrrrrr..rr.rrrrrrrrrrrrrr.............rrrr.rr.rrr................",
        "........r..rrrrr..g...gg.g.g....g.....rrrrrrrrrrrrr..rr..rrrrrrrrrrrr..............rrrrrrr.rrr................",
        "........r..rrrrr...gg.....g.....g.....rrrrr.rrr.rrr...rr.rrrrrrrrrrr..r............rrrrrrr.rr.................",
        "........rr.rrrrr....ggg...g...........rrrr.rrrr.rrrr...rr.rrrrrrrrr....r..........rrrr.rrr.rr.................",
        ".........rrrrrrr.....ggg.gg...............rrrr..rrrr....rr..rrrrr......r..........rrrrrrrr.rr.................",
        "..........rrrrrr......gg.g..................r..rrrrrr...............rr............rrr.rrrr.rr.................",
        ".........rrrrrrrr........g.g....g.........r..rrrrrrrrrr..........................rrrrrrrrr.r..................",
        ".........rrrrrrr.........gggggg..g...g....rrrrrrrrrrrrrrr.......................rrrr.rrrrr.r..................",
        ".........rrrrrr.......g..gggg..ggg..gg...rr.rrrrrrrrrrrrrr.rr..................rrrr.rrrrrr....................",
        ".........rrrrrr.......g......gg.gg..gg..rr..rrrrrrrrrrrrr.r.rrrr..............rrrr.rrrrrrr....................",
        ".........rrrrrr...gg.....gg......g......r..rrrrrrrrrrrrrrrrrr..rrr..........rrrr.rrrrrrrrr...g................",
        ".........rrrrrr..gggg.....gggg.g...............rrrrrrr..rrrr.rr...rrrrrrrrrrrrr.rrrrr.rrr...ggg...............",
        "..........rrrrr...ggggg.....gggg...............rrrrrrrr..rrrr.rrrrr..........rrrrrrr..rrr....gg...............",
        "...........rrr...g.gggggg.........gggg.......r.rrrrrrrr...rrrrr.rrrrrrrrrrrrrrrrrr..rrrr...g..gg..............",
        ".......r....rr...gg....ggggggggggggggg...g..r....rrrrrrrr..rrrrr..rrrrrrrrrrrrrrr..rrrrr...gg..gg.............",
        "........r....rr..ggggggg..gggggggggggg..gg........rrrrrrrr..rrrrrr...rrrrrrrrr....rrrrr....ggg..gg............",
        "........r.....r...ggg...gg.ggggggggg....g......r..rrrrrrrrr...rrrrr..............rrrrr.g....gg...gg...........",
        "....................ggg..g..............gg.....r..rrrrrrrrrr...rrrrrr..........rrrrr..ggg....gg..gg...........",
        ".................r....ggggggggg.........gg.....r..rrrrrrrrrrr...rrrrrrr.....rrrrrr....gggg....gg..gg..........",
        ".................rr.................gg...gg......rrrrrrrrrrrrr...rrrrrrrrrrrrrrrr.gg..ggggg...gg..gg..........",
        "..........rrr.....r.................ggg..gggg....rrrrrrrrrrrrrr...rrrrrrrrrrrrrr.ggg...gggg....gg..g..........",
        "...........rr.....r................ggggg.gggg...rrrrrrrrrrrrrrrr...rrrrrrrrrrrrr.gggg...ggg....gg..gg.........",
        "............r......................ggggg.gggg..rrrr.rrrrrr...rrr.....rrrrrrrrrr.gggggg...g..g...g..g..........",
        "............r.................gggggggggg.gggg..rrr.rrrrr.....rrr.......rrrrrrrr..gggggg....gggg...gg..........",
        ".............................gggggggggg.gggg......rrrrrr.....rr...........rrrrr...gggggg....ggggggg...........",
        "............................gggggggg....gggg.....rrrrrrr....rr....rrrrrr...rrrr....gggggg......ggg............",
        "...........................ggggggggg.ggggggg.....rrrrrrr...rrr...rrrrrrrrr..rr......ggg.......................",
        "...........................gg....ggg.ggggggg....rrrrrrr...r....rrrrrrrrrrrr.rr.......gg.ggg.....gg............",
        "...........................gg.gg..gg.ggggggg....rrrrrr........rrrr......rrr..r..........gggggggggg............",
        ".............................ggggg.....gggggg................rrrr..gggg..rrr............ggggggggg.............",
        ".............................gggggg....g...g.g.............rrrr..ggggggg..rr..............ggggggg.............",
        "............................ggggggg.ggg..g.........rrrrrrrrrrr...gggggggg.rr..................................",
        "............................ggggggg.ggg.gg.gg.gg...rrrrrrrrrr....gggggggg..r..................................",
        "...........................gggggggg.ggg.g..gg.g....rrrrrrrr......ggggggggg.r.r................................",
        "..........................ggggggggg...............................ggggggggg...................................",
        "........................gggggggggg.................................gggggggggg.................................",
        "......................ggggggggggg...................................gggggggggg................................",
        "...................ggggggggggggg................................gg...gggggggggggg.............................",
        ".................gggggggggggggg...gg............................gg.....gggggggggggg...........................",
        "..............ggggggggggggggg.....ggg..........................ggg......ggggggggggggggggg.....................",
        "...............ggggggggggg........gggg.....g..................gggg.........gggggggggggggg.....................",
        "...........................rr.....ggggg.gg....rrrrrrrrrr...g.ggggg.....rr.....ggggggg.........................",
        "........................rrrrr......gggg....rrrrrrrrrrrrrrr...gggg......rrrr...................................",
        "..........................rrr......gggg..rrrrrrrrrrrrrrrrrrr...g.......rrr....................................",
        "..........................rrrr.........rrrrrrrrr.....rrrrrrrrr.......rrrrr....................................",
        "...........................rrrrrr....rrrrrrrr...........rrrrrrrr...rrrrrr.....................................",
        "...........................rrrrrrrrrrrrrrr................rrrrrrrrrrrrrrr.....................................",
        ".............................rrrrrrrrrrr....................rrrrrrrrrrrr......................................",
        "...............................rrrrrr...rr...rrrrrrrrrrr.rrr...rrrrrrr........................................",
        ".....................................rrrr...rrrrrrrrrrrrr.rrrr................................................",
        "....................................rrrrr..rrrrrrrrrrrrrr..rrrrr..............................................",
        "....................................rrrr..rrrrrrrrrrrrrrrr.rrrr...............................................",
        "......................................r.rrrrrrrrrrrrrrrrrr..rr................................................",
        "........................................rrrrrrrrrrrrrrrrrrr...................................................",
        "........................................rrrrrrrrrrrrrrrrrrr...................................................",
        ".........................................rrrrrrrrrrrrrrrrr....................................................",
        "...........................................rrrrrr.rrrrrrr.....................................................",
        "...............................................r....r.........................................................",
        ".............................................................................................................."
    };

    // two bits a cell, sixteen cells a word
    public static string Hlsl()
    {
        var table = new StringBuilder($"static const uint MaskBits[{Lines * WordsPerLine}] = {{");
        foreach (var row in Rows)
        {
            for (int word = 0; word < WordsPerLine; word++)
            {
                uint bits = 0;
                for (int cell = 0; cell < 16 && (word * 16) + cell < row.Length; cell++)
                {
                    char c = row[(word * 16) + cell];
                    bits |= (c == 'r' ? 1u : c == 'g' ? 2u : 0u) << (cell * 2);
                }

                table.Append($"{bits}u,");
            }
        }

        return table.Append("};\n").ToString();
    }
}
