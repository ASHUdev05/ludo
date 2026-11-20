using Godot;
using System;

// Shown before a special game starts, so everyone knows what is different.
public partial class RulesScreen : CanvasLayer
{
    public event Action Closed;

    // Set before adding to the tree
    public RuleSet Rules = RuleSet.Classic;

    public static string TitleFor(RuleSet rules) => rules switch
    {
        RuleSet.ThreeSixes => "THREE SIXES",
        RuleSet.Deathmatch => "DEATHMATCH",
        RuleSet.Supermatch => "SUPERMATCH",
        _ => "CLASSIC"
    };

    public static string[] RulesFor(RuleSet rules)
    {
        switch (rules)
        {
            case RuleSet.ThreeSixes:
                return new[]
                {
                    "A 6 gives you another roll, as usual.",
                    "Roll three 6s in a row and the third 6 is void.",
                    "Your turn ends at once, and the moves you made with the first two 6s are undone.",
                    "Two tokens of the same colour on a cell form a blockade: opponents cannot land on it or pass it (not on stars or start squares).",
                    "Everything else follows the classic rules."
                };

            case RuleSet.Deathmatch:
                return new[]
                {
                    "Every player starts with all 4 tokens already out, on their start square.",
                    "No 6 is needed to begin. Roll and fight!",
                    "Landing on an opponent captures it, except on stars and start squares.",
                    "A captured token goes back to the yard and needs a 6 to come out again.",
                    "A 6, a capture, or a token reaching home gives another turn.",
                    "Two tokens of the same colour on a cell form a blockade: opponents cannot land on it or pass it (not on stars or start squares).",
                    "Bring all 4 tokens home to win."
                };

            case RuleSet.Supermatch:
                return new[]
                {
                    "Every player starts with all 4 tokens already out, on their start square.",
                    "Each token has its own speed multiplier. Reach the next player's start square and it becomes x2, the one after x3, and so on.",
                    "Your dice roll is multiplied by that token's multiplier. A badge on the token shows it.",
                    "A move that would go past home simply finishes at home.",
                    "A captured token loses its multiplier and goes back to the yard (a 6 brings it out).",
                    "Two tokens of the same colour on a cell form a blockade: opponents cannot land on it or pass it (not on stars or start squares).",
                    "Bring all 4 tokens home to win."
                };

            default:
                return Array.Empty<string>();
        }
    }

    public override void _Ready()
    {
        Layer = 15;

        var dim = new ColorRect
        {
            Color = new Color(0.05f, 0.06f, 0.08f, 0.96f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(840, 0) };
        box.AddThemeConstantOverride("separation", 28);
        center.AddChild(box);

        var title = new Label
        {
            Text = TitleFor(Rules),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 96);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 14);
        box.AddChild(title);

        var subtitle = new Label
        {
            Text = "How this mode works",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        subtitle.AddThemeFontSizeOverride("font_size", 36);
        subtitle.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        box.AddChild(subtitle);

        foreach (string rule in RulesFor(Rules))
        {
            var line = new Label
            {
                Text = "•  " + rule,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(840, 0)
            };
            line.AddThemeFontSizeOverride("font_size", 36);
            box.AddChild(line);
        }

        var start = new Button
        {
            Text = "Let's play!",
            CustomMinimumSize = new Vector2(840, 110)
        };
        start.AddThemeFontSizeOverride("font_size", 48);
        start.Pressed += () =>
        {
            Closed?.Invoke();
            QueueFree();
        };
        box.AddChild(start);
    }
}
