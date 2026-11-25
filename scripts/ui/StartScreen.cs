using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Full-screen setup menu: pick the player mode, the game type, then set each seat
// to Human / Bot / Off. Scrolls if the screen is short.
public partial class StartScreen : CanvasLayer
{
    public event Action<GameConfig> Started;

    // Set before adding to the tree
    public GameConfig Config = new GameConfig();

    private readonly Dictionary<GameMode, Button> _modeButtons = new();
    private readonly Dictionary<RuleSet, Button> _ruleButtons = new();
    private readonly Dictionary<string, Button> _seatButtons = new();
    private readonly Dictionary<string, Label> _seatNames = new();

    private Label _ruleDescription;
    private Label _hint;
    private Button _startButton;

    private static string ModeName(GameMode mode) => mode switch
    {
        GameMode.TwoPlayers => "2 Players",
        GameMode.ThreePlayers => "3 Players",
        GameMode.FourPlayers => "4 Players",
        _ => "2 vs 2 Teams"
    };

    private static string RuleName(RuleSet rules) => rules switch
    {
        RuleSet.ThreeSixes => "Three Sixes",
        RuleSet.Deathmatch => "Deathmatch",
        RuleSet.Supermatch => "Supermatch",
        _ => "Classic"
    };

    private static string RuleSummary(RuleSet rules) => rules switch
    {
        RuleSet.ThreeSixes => "Three 6s in a row lose your whole streak and your turn.",
        RuleSet.Deathmatch => "Everyone starts with all tokens already out.",
        RuleSet.Supermatch => "All tokens out, plus x2, x3... speed boosts.",
        _ => "Standard Ludo rules."
    };

    public override void _Ready()
    {
        Layer = 20;

        var bg = new ColorRect
        {
            Color = new Color(0.07f, 0.08f, 0.10f, 0.97f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Scrolls vertically if the content is taller than the screen
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(center);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(800, 0) };
        box.AddThemeConstantOverride("separation", 24);
        center.AddChild(box);

        // Title
        var title = MakeLabel("LUDO", 130, Colors.White);
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 12);
        box.AddChild(title);

        // Players
        box.AddChild(MakeLabel("Players", 38, new Color(0.8f, 0.8f, 0.8f)));

        var modeGrid = MakeGrid();
        box.AddChild(modeGrid);

        var modeGroup = new ButtonGroup();
        foreach (GameMode mode in Enum.GetValues<GameMode>())
        {
            GameMode m = mode;
            var btn = MakeToggle(ModeName(m), modeGroup);
            btn.Toggled += on => { if (on) SetMode(m); };
            modeGrid.AddChild(btn);
            _modeButtons[m] = btn;
        }

        // Game type
        box.AddChild(MakeLabel("Game type", 38, new Color(0.8f, 0.8f, 0.8f)));

        var ruleGrid = MakeGrid();
        box.AddChild(ruleGrid);

        var ruleGroup = new ButtonGroup();
        foreach (RuleSet rules in Enum.GetValues<RuleSet>())
        {
            RuleSet r = rules;
            var btn = MakeToggle(RuleName(r), ruleGroup);
            btn.Toggled += on => { if (on) SetRules(r); };
            ruleGrid.AddChild(btn);
            _ruleButtons[r] = btn;
        }

        _ruleDescription = MakeLabel("", 30, new Color(0.9f, 0.85f, 0.5f));
        _ruleDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _ruleDescription.CustomMinimumSize = new Vector2(800, 0);
        box.AddChild(_ruleDescription);

        // Seats
        box.AddChild(MakeLabel("Seats - tap to switch", 38, new Color(0.8f, 0.8f, 0.8f)));

        foreach (string color in GameConfig.Order)
        {
            string c = color;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 20);
            box.AddChild(row);

            row.AddChild(new ColorRect
            {
                Color = DiceButton.ColorFor(c),
                CustomMinimumSize = new Vector2(70, 70)
            });

            var name = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center
            };
            name.AddThemeFontSizeOverride("font_size", 42);
            row.AddChild(name);
            _seatNames[c] = name;

            var seat = new Button { CustomMinimumSize = new Vector2(300, 80) };
            seat.AddThemeFontSizeOverride("font_size", 36);
            seat.Pressed += () => CycleSeat(c);
            row.AddChild(seat);
            _seatButtons[c] = seat;
        }

        _hint = MakeLabel("", 32, Colors.White);
        box.AddChild(_hint);

        _startButton = new Button
        {
            Text = "Start game",
            CustomMinimumSize = new Vector2(800, 110)
        };
        _startButton.AddThemeFontSizeOverride("font_size", 48);
        _startButton.Pressed += () => Started?.Invoke(Config);
        box.AddChild(_startButton);

        var settings = new Button
        {
            Text = "Settings",
            CustomMinimumSize = new Vector2(800, 80)
        };
        settings.AddThemeFontSizeOverride("font_size", 36);
        settings.Pressed += () => SettingsMenu.Open(this);
        box.AddChild(settings);

        // Show the remembered choices without resetting the remembered seats
        _modeButtons[Config.Mode].SetPressedNoSignal(true);
        _ruleButtons[Config.Rules].SetPressedNoSignal(true);
        Refresh();
    }

    private static GridContainer MakeGrid()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 20);
        grid.AddThemeConstantOverride("v_separation", 20);
        return grid;
    }

    private static Button MakeToggle(string text, ButtonGroup group)
    {
        var btn = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonGroup = group,
            CustomMinimumSize = new Vector2(390, 90)
        };
        btn.AddThemeFontSizeOverride("font_size", 34);
        return btn;
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private void SetRules(RuleSet rules)
    {
        Config.Rules = rules;
        Refresh();
    }

    // Choosing a player mode sets sensible default seats
    private void SetMode(GameMode mode)
    {
        Config.Mode = mode;

        string[] active = mode switch
        {
            GameMode.TwoPlayers => new[] { "Red", "Yellow" },
            GameMode.ThreePlayers => new[] { "Red", "Green", "Yellow" },
            _ => GameConfig.Order
        };

        foreach (string c in GameConfig.Order)
        {
            if (!active.Contains(c))
                Config.Seats[c] = SeatType.Off;
            else if (Config.Seats[c] == SeatType.Off)
                Config.Seats[c] = SeatType.Human;
        }

        Refresh();
    }

    private void CycleSeat(string color)
    {
        // Only 2 and 3 player games may leave seats empty
        bool canBeOff = Config.Mode is GameMode.TwoPlayers or GameMode.ThreePlayers;

        Config.Seats[color] = Config.Seats[color] switch
        {
            SeatType.Human => SeatType.Bot,
            SeatType.Bot => canBeOff ? SeatType.Off : SeatType.Human,
            _ => SeatType.Human
        };

        Refresh();
    }

    private void Refresh()
    {
        _ruleDescription.Text = RuleSummary(Config.Rules);

        foreach (string c in GameConfig.Order)
        {
            SeatType type = Config.Seats[c];

            _seatButtons[c].Text = type.ToString();
            _seatButtons[c].Modulate = type == SeatType.Off
                ? new Color(1, 1, 1, 0.45f)
                : Colors.White;

            string teamTag = Config.IsTeams
                ? (c == "Red" || c == "Yellow" ? "  (Team 1)" : "  (Team 2)")
                : "";

            _seatNames[c].Text = c + teamTag;
            _seatNames[c].Modulate = type == SeatType.Off
                ? new Color(1, 1, 1, 0.45f)
                : Colors.White;
        }

        int count = Config.ActiveColors.Count();
        int needed = Config.RequiredPlayers;
        bool valid = count == needed;

        if (!valid)
        {
            _hint.Text = $"Pick exactly {needed} seats (currently {count})";
            _hint.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f));
        }
        else if (Config.IsTeams)
        {
            _hint.Text = "Red + Yellow  vs  Green + Blue";
            _hint.AddThemeColorOverride("font_color", Colors.White);
        }
        else
        {
            _hint.Text = "Ready to play";
            _hint.AddThemeColorOverride("font_color", new Color(0.6f, 1f, 0.6f));
        }

        _startButton.Disabled = !valid;
    }
}
