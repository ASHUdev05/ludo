using Godot;
using System;
using System.Collections.Generic;

// End-of-game overlay: winner title, final standings, confetti, Rematch / Main menu.
// Laid out with anchors, so it is always centered whatever the window size is.
public partial class WinScreen : CanvasLayer
{
    public event Action RematchPressed;
    public event Action MenuPressed;

    // Set before adding to the tree
    public string TitleText = "";
    public string ColorName = "Red";
    public List<(string Color, int Rank)> Standings = new();

    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Layer = 10;
        _rng.Randomize();

        Vector2 view = GetViewport().GetVisibleRect().Size;

        // Dim background (also blocks clicks on the board behind it)
        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.CreateTween().TweenProperty(dim, "color:a", 0.65f, 0.3f);

        SpawnConfetti(view);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(800, 0) };
        panel.AddThemeConstantOverride("separation", 36);
        panel.Modulate = new Color(1, 1, 1, 0);
        center.AddChild(panel);

        var title = new Label
        {
            Text = TitleText,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 96);
        title.AddThemeColorOverride("font_color", DiceButton.ColorFor(ColorName));
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 16);
        panel.AddChild(title);

        // Final standings
        foreach (var entry in Standings)
        {
            var line = new Label
            {
                Text = $"{Ordinal(entry.Rank)}   {entry.Color}",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            line.AddThemeFontSizeOverride("font_size", 52);
            line.AddThemeColorOverride("font_color", DiceButton.ColorFor(entry.Color));
            line.AddThemeColorOverride("font_outline_color", Colors.Black);
            line.AddThemeConstantOverride("outline_size", 10);
            panel.AddChild(line);
        }

        panel.AddChild(MakeButton("Rematch", () => RematchPressed?.Invoke()));
        panel.AddChild(MakeButton("Main menu", () => MenuPressed?.Invoke()));

        // Fade in
        panel.CreateTween().TweenProperty(panel, "modulate:a", 1f, 0.35f);
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th"
    };

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(420, 100),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        button.AddThemeFontSizeOverride("font_size", 44);
        button.Pressed += () => onPressed();
        return button;
    }

    private void SpawnConfetti(Vector2 view)
    {
        Color[] palette =
        {
            DiceButton.ColorFor("Red"), DiceButton.ColorFor("Green"),
            DiceButton.ColorFor("Yellow"), DiceButton.ColorFor("Blue"),
            Colors.White
        };

        for (int i = 0; i < 90; i++)
        {
            var piece = new ColorRect
            {
                Color = palette[_rng.RandiRange(0, palette.Length - 1)],
                Size = new Vector2(_rng.RandfRange(10f, 18f), _rng.RandfRange(16f, 26f)),
                Position = new Vector2(_rng.RandfRange(0f, view.X), -40f),
                Rotation = _rng.RandfRange(0f, Mathf.Tau),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            AddChild(piece);

            float delay = _rng.RandfRange(0f, 1.0f);
            float fall = _rng.RandfRange(2.2f, 4.0f);
            float drift = _rng.RandfRange(-120f, 120f);

            var tween = piece.CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(piece, "position:y", view.Y + 60f, fall).SetDelay(delay);
            tween.TweenProperty(piece, "position:x", piece.Position.X + drift, fall).SetDelay(delay);
            tween.TweenProperty(piece, "rotation", piece.Rotation + _rng.RandfRange(-8f, 8f), fall).SetDelay(delay);
            tween.Chain().TweenCallback(Callable.From(piece.QueueFree));
        }
    }
}
