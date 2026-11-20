using Godot;
using System;
using System.Collections.Generic;

// A Ludo token. The shape depends on the color (see TokenShapes):
//   Red = pawn, Green = diamond, Yellow = heart, Blue = orb
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

    // Drawn size; BoardManager sets this from the cell size
    public float Radius = 22f;

    private bool _highlighted = false;
    private float _ringAngle = 0f;
    private float _lift = 0f; // how far the token is raised while hopping
    private int _multiplier = 1;

    // One tween per kind of motion, so a new move always replaces the old one
    private Tween _moveTween;
    private Tween _scaleTween;

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

    // Supermatch speed multiplier. Shown as a small badge when above 1.
    public int Multiplier
    {
        get => _multiplier;
        set
        {
            if (_multiplier == value) return;
            _multiplier = value;
            QueueRedraw();
        }
    }

    // GameManager listens to this
    public event Action<Token> OnTokenClicked;

    public override void _Ready()
    {
        SetProcess(false);

        // Generous circular tap area, good for touch screens
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

    // =====================================
    // DRAWING
    // =====================================

    public override void _Draw()
    {
        float r = Radius;
        float k = r / 22f;

        // Dashed ring on the ground around movable tokens
        if (_highlighted)
        {
            DrawDashedRing(r * 1.55f, Colors.Black, 6f * k);
            DrawDashedRing(r * 1.55f, Colors.White, 3.5f * k);
        }

        // Ground shadow (shrinks while the token is in the air)
        float shrink = 1f - Mathf.Clamp(_lift / (r * 1.6f), 0f, 0.35f);
        DrawColoredPolygon(
            TokenShapes.Ellipse(new Vector2(0, r * 0.98f), r * 0.95f * shrink, r * 0.26f * shrink),
            new Color(0, 0, 0, 0.35f));

        // Everything below is lifted together while hopping
        DrawSetTransform(new Vector2(0, -_lift), 0f, Vector2.One);

        TokenShapes.Draw(this, PlayerColor, Vector2.Zero, r);

        if (_multiplier > 1)
            DrawBadge(r, k);

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // Small "x2" / "x3" / "x4" badge at the top right of the token
    private void DrawBadge(float r, float k)
    {
        Vector2 center = new Vector2(r * 0.85f, -r * 1.0f);
        float badgeRadius = r * 0.58f;

        Color fill = _multiplier switch
        {
            2 => new Color(1.00f, 0.92f, 0.35f),
            3 => new Color(1.00f, 0.60f, 0.15f),
            _ => new Color(0.95f, 0.25f, 0.25f)
        };

        DrawCircle(center, badgeRadius, fill);
        DrawArc(center, badgeRadius, 0f, Mathf.Tau, 20, Colors.Black, 2f * k, true);

        Font font = ThemeDB.FallbackFont;
        int fontSize = Mathf.Max(8, (int)(badgeRadius * 1.15f));
        Vector2 pos = new Vector2(center.X - badgeRadius, center.Y + fontSize * 0.35f);

        DrawString(font, pos, $"x{_multiplier}", HorizontalAlignment.Center, badgeRadius * 2f, fontSize, Colors.Black);
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

    private void SetLift(float value)
    {
        _lift = value;
        QueueRedraw();
    }

    // =====================================
    // SMOOTH MOTION
    // =====================================

    private void KillMotion()
    {
        if (_moveTween != null && _moveTween.IsValid())
            _moveTween.Kill();

        SetLift(0f);
    }

    // Slide to a position and size. Any earlier slide is replaced, so tweens never fight.
    public void GlideTo(Vector2 position, float scale, float duration = 0.22f, bool overshoot = false)
    {
        ScaleTo(scale, duration, overshoot);

        if (Position.DistanceTo(position) < 0.5f)
        {
            KillMotion();
            Position = position;
            return;
        }

        KillMotion();
        _moveTween = CreateTween();
        _moveTween.TweenProperty(this, "position", position, duration)
            .SetTrans(overshoot ? Tween.TransitionType.Back : Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    public void ScaleTo(float scale, float duration = 0.2f, bool overshoot = false)
    {
        if (_scaleTween != null && _scaleTween.IsValid())
            _scaleTween.Kill();

        Vector2 target = new Vector2(scale, scale);

        if (Scale.DistanceTo(target) < 0.01f)
        {
            Scale = target;
            return;
        }

        _scaleTween = CreateTween();
        _scaleTween.TweenProperty(this, "scale", target, duration)
            .SetTrans(overshoot ? Tween.TransitionType.Back : Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    // One continuous tween along the whole path (no stop-start between cells).
    // The token arcs up and down on every cell, and onStep fires as it lands on each.
    public Tween MoveAlongPath(IReadOnlyList<Vector2> points, float stepDuration = 0.10f, Action onStep = null)
    {
        KillMotion();

        int n = points.Count;
        var path = new Vector2[n + 1];
        path[0] = Position;
        for (int i = 0; i < n; i++) path[i + 1] = points[i];

        int stepsDone = 0;

        _moveTween = CreateTween();
        _moveTween.TweenMethod(Callable.From<float>(t =>
            {
                int segment = Mathf.Min((int)t, n - 1);
                float f = Mathf.Clamp(t - segment, 0f, 1f);

                Position = path[segment].Lerp(path[segment + 1], f);
                SetLift(Mathf.Sin(f * Mathf.Pi) * Radius * 0.6f);

                int crossed = (int)t;
                while (stepsDone < crossed && stepsDone < n)
                {
                    stepsDone++;
                    onStep?.Invoke();
                }
            }), 0f, (float)n, n * stepDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);

        _moveTween.TweenCallback(Callable.From(() => SetLift(0f)));

        return _moveTween;
    }
}