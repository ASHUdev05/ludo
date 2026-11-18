using Godot;
using System;
using System.Collections.Generic;

public partial class Token : Area2D
{
    [Export] public string PlayerColor = "Red";
    [Export] public int TokenId = 0;

    // -1 = in yard, 0..50 = main track, 51..55 = home lane, 56 = finished
    public int StepsTaken = -1;

    // Where this token sits in its yard (local to the Tokens node)
    public Vector2 YardPosition;

    public bool IsInYard => StepsTaken < 0;
    public bool IsFinished => StepsTaken >= 56;

    // Drawn size; BoardManager sets this from the cell size so tokens scale with the board
    public float Radius = 22f;

    private bool _highlighted = false;
    private float _ringAngle = 0f;

    // Movable tokens get a rotating dashed ring
    public bool Highlighted
    {
        get => _highlighted;
        set
        {
            _highlighted = value;
            SetProcess(value);
            QueueRedraw();
        }
    }

    // GameManager listens to this
    public event Action<Token> OnTokenClicked;

    private Color GetColorCode()
    {
        return PlayerColor switch
        {
            "Red" => Colors.Red,
            "Green" => Colors.ForestGreen,
            "Yellow" => Colors.Gold,
            "Blue" => Colors.DodgerBlue,
            _ => Colors.White
        };
    }

    public override void _Ready()
    {
        SetProcess(false);

        // Size the tap area to the token (generous, for touch screens)
        var shape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        if (shape != null)
            shape.Shape = new CircleShape2D { Radius = Radius * 1.4f };
    }

    public override void _Process(double delta)
    {
        if (!_highlighted) return;

        _ringAngle += (float)delta * 2.0f;
        QueueRedraw();
    }

    public override void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            OnTokenClicked?.Invoke(this);
        }
    }

    public override void _Draw()
    {
        float k = Radius / 22f;

        if (_highlighted)
        {
            // Black underlay + white dashes so the ring reads on any colour
            DrawDashedRing(Radius * 1.41f, Colors.Black, 6f * k);
            DrawDashedRing(Radius * 1.41f, Colors.White, 3.5f * k);
        }

        DrawCircle(Vector2.Zero, Radius, GetColorCode());
        DrawCircle(Vector2.Zero, Radius, Colors.Black, false, 3f * k);
    }

    private void DrawDashedRing(float radius, Color color, float width)
    {
        const int dashes = 10;
        float step = Mathf.Tau / dashes;

        for (int i = 0; i < dashes; i++)
        {
            float start = _ringAngle + i * step;
            DrawArc(Vector2.Zero, radius, start, start + step * 0.55f, 8, color, width, true);
        }
    }

    // Straight slide
    public Tween MoveToPosition(Vector2 targetPos, float duration = 0.25f)
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "position", targetPos, duration);
        return tween;
    }

    // Hops cell by cell so the token follows the track around corners
    // onStep (optional) is called each time the token lands on a cell
    public Tween MoveAlongPath(IEnumerable<Vector2> points, float stepDuration = 0.15f, Action onStep = null)
    {
        var tween = CreateTween();
        foreach (Vector2 p in points)
        {
            tween.TweenProperty(this, "position", p, stepDuration);

            if (onStep != null)
                tween.TweenCallback(Callable.From(onStep));
        }
        return tween;
    }
}