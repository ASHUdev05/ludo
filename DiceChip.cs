using Godot;
using System;

// A small die face showing one saved roll. Tap it to choose which roll to play next.
public partial class DiceChip : Control
{
    public event Action Clicked;

    // Set before adding to the tree
    public int Value = 1;
    public string PlayerColor = "Red";
    public bool Selected = false;
    public bool Usable = true;

    private StyleBoxFlat _face;
    private StyleBoxFlat _glow;

    public override void _Ready()
    {
        MouseDefaultCursorShape = Usable ? CursorShape.PointingHand : CursorShape.Arrow;

        // Rolls that can't be played right now are dimmed
        Modulate = Usable ? Colors.White : new Color(1, 1, 1, 0.4f);

        Color color = DiceButton.ColorFor(PlayerColor);
        int radius = (int)(Size.X * 0.22f);

        _face = new StyleBoxFlat { BgColor = Colors.White, BorderColor = color };
        _face.SetBorderWidthAll(Mathf.Max(3, (int)(Size.X * 0.07f)));
        _face.SetCornerRadiusAll(radius);

        _glow = new StyleBoxFlat { DrawCenter = false, BorderColor = new Color(1f, 0.95f, 0.4f) };
        _glow.SetBorderWidthAll(5);
        _glow.SetCornerRadiusAll(radius + 4);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Usable) return;

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            AcceptEvent();
            Clicked?.Invoke();
        }
    }

    public override void _Draw()
    {
        if (_face == null) return;

        Rect2 rect = new Rect2(Vector2.Zero, Size);

        if (Selected)
            DrawStyleBox(_glow, rect.Grow(5f));

        DrawStyleBox(_face, rect);

        float pipRadius = Size.X * 0.075f;
        foreach (Vector2 p in GetPips(Value))
            DrawCircle(p * Size, pipRadius, new Color(0.08f, 0.08f, 0.08f));
    }

    private static Vector2[] GetPips(int value)
    {
        const float a = 0.28f, b = 0.5f, c = 0.72f;

        Vector2 tl = new(a, a), tr = new(c, a);
        Vector2 ml = new(a, b), mr = new(c, b), mid = new(b, b);
        Vector2 bl = new(a, c), br = new(c, c);

        return value switch
        {
            1 => new[] { mid },
            2 => new[] { tl, br },
            3 => new[] { tl, mid, br },
            4 => new[] { tl, tr, bl, br },
            5 => new[] { tl, tr, mid, bl, br },
            _ => new[] { tl, tr, ml, mr, bl, br }
        };
    }
}
