using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ProfileRunner;

static class Banner
{
    // "ANSI Shadow" lettering: solid blocks with a box-drawing outline that reads as a drop shadow.
    // Only the letters of the banners in use are defined.
    static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['E'] = ["███████╗", "██╔════╝", "█████╗  ", "██╔══╝  ", "███████╗", "╚══════╝"],
        ['F'] = ["███████╗", "██╔════╝", "█████╗  ", "██╔══╝  ", "██║     ", "╚═╝     "],
        ['I'] = ["██╗", "██║", "██║", "██║", "██║", "╚═╝"],
        ['L'] = ["██╗     ", "██║     ", "██║     ", "██║     ", "███████╗", "╚══════╝"],
        ['N'] = ["███╗   ██╗", "████╗  ██║", "██╔██╗ ██║", "██║╚██╗██║", "██║ ╚████║", "╚═╝  ╚═══╝"],
        ['O'] = [" ██████╗ ", "██╔═══██╗", "██║   ██║", "██║   ██║", "╚██████╔╝", " ╚═════╝ "],
        ['P'] = ["██████╗ ", "██╔══██╗", "██████╔╝", "██╔═══╝ ", "██║     ", "╚═╝     "],
        ['R'] = ["██████╗ ", "██╔══██╗", "██████╔╝", "██╔══██╗", "██║  ██║", "╚═╝  ╚═╝"],
        ['U'] = ["██╗   ██╗", "██║   ██║", "██║   ██║", "██║   ██║", "╚██████╔╝", " ╚═════╝ "],
    };

    const int Height = 6;

    const string OutlineColor = "#767676";

    public static IRenderable Create(string text)
    {
        var markup = new StringBuilder();
        for (var row = 0; row < Height; row++)
        {
            // Blocks fade from light to dark grey top to bottom; the outline keeps one mid grey.
            var shade = (byte)(228 - row * 24);
            var blockColor = new Color(shade, shade, shade).ToMarkup();
            var line = string.Concat(text.Select(letter => Glyphs[letter][row]));
            markup.Append(Regex.Replace(line, "█+|[^█]+",
                run => $"[{(run.Value[0] == '█' ? blockColor : OutlineColor)}]{run.Value}[/]"));
            markup.Append('\n');
        }

        return new Markup(markup.ToString());
    }
}
