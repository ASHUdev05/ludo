using Godot;
using System;

// A die that is also the roll button: click it, it spins and shuffles faces,
// then settles on the final value.
public partial class DiceButton : Control
{
    public event Action Clicked;
    public event Action RollFinished;

    public string PlayerColor = "Red";
    public int Value { get; private set; } = 1;

    public const float RollDuration = 0.5f;

    private bool _canRoll = false;
    private bool _isTurn = false;
    private bool _rolling = false;

    private int _shownValue = 1;
    private int _finalValue = 1;
    private float _time = 0f;
    private float _shuffleTimer = 0f;

    private readonly RandomNumberGenerator _rng = new();
    private StyleBoxFlat _faceStyle;
    private StyleBoxFlat _glowStyle;

    // Click-to-roll is only allowed when true (also shows the pulsing glow)
    public bool CanRoll
    {
        get => _canRoll;
        set { _canRoll = value; QueueRedraw(); }
    }

    // True for the player whose turn it is (full opacity); others are dimmed
    public bool IsTurn
    {
        get => _isTurn;
        set
        {
            _isTurn = value;
            Modulate = value ? Colors.White : new Color(1, 1, 1, 0.4f);
        }
    }

    public static Color ColorFor(string playerColor)
    {
        return playerColor switch
        {
            "Red" => new Color(0.90f, 0.04f, 0.04f),
            "Green" => new Color(0.02f, 0.60f, 0.12f),
            "Yellow" => new Color(0.96f, 0.82f, 0.00f),
            "Blue" => new Color(0.00f, 0.65f, 0.95f),
            _ => Colors.White
        };
    }

    public override void _Ready()
    {
        _rng.Randomize();
        PivotOffset = Size / 2f;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        Modulate = new Color(1, 1, 1, 0.4f);

        Color c = ColorFor(PlayerColor);

        _faceStyle = new StyleBoxFlat { BgColor = Colors.White, BorderColor = c };
        _faceStyle.SetBorderWidthAll(8);
        _faceStyle.SetCornerRadiusAll(24);

        _glowStyle = new StyleBoxFlat { DrawCenter = false, BorderColor = c };
        _glowStyle.SetBorderWidthAll(5);
        _glowStyle.SetCornerRadiusAll(34);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        if (_rolling)
        {
            _shuffleTimer -= (float)delta;
            if (_shuffleTimer <= 0f)
            {
                _shuffleTimer = 0.05f;

                int next = _shownValue;
                while (next == _shownValue)
                    next = _rng.RandiRange(1, 6);
                _shownValue = next;
            }
        }

        if (_canRoll || _rolling)
            QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!_canRoll || _rolling) return;

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            AcceptEvent();
            Clicked?.Invoke();
        }
    }

    // Spin + bounce + face shuffle, then settle on finalValue
    public void Roll(int finalValue)
    {
        _rolling = true;
        _canRoll = false;
        _finalValue = finalValue;
        _shuffleTimer = 0f;
        Modulate = Colors.White;

        var spin = CreateTween();
        spin.TweenProperty(this, "rotation", Mathf.Tau * 2f, RollDuration)
            .From(0f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        spin.TweenCallback(Callable.From(FinishRoll));

        var bounce = CreateTween();
        bounce.TweenProperty(this, "scale", new Vector2(1.25f, 1.25f), RollDuration * 0.4f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        bounce.TweenProperty(this, "scale", Vector2.One, RollDuration * 0.6f)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
    }

    private void FinishRoll()
    {
        Rotation = 0f;
        Scale = Vector2.One;
        _rolling = false;
        Value = _finalValue;
        _shownValue = _finalValue;
        QueueRedraw();
        RollFinished?.Invoke();
    }

    public override void _Draw()
    {
        if (_faceStyle == null) return;

        Rect2 rect = new Rect2(Vector2.Zero, Size);

        if (_canRoll && !_rolling)
        {
            float pulse = 4f + 4f * Mathf.Sin(_time * 5f);
            DrawStyleBox(_glowStyle, rect.Grow(6f + pulse));
        }

        DrawStyleBox(_faceStyle, rect);

        float pipRadius = Size.X * 0.075f;
        foreach (Vector2 p in GetPips(_shownValue))
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