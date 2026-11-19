using Godot;
using System;
using System.Collections.Generic;

// A Ludo token. Each color has its own shape so players can tell them apart
// without relying on color alone:
//   Red = pawn, Green = diamond, Yellow = star, Blue = orb
public partial class Token : Area2D
{
    private enum Shape { Pawn, Diamond, Star, Orb }

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

    // GameManager listens to this
    public event Action<Token> OnTokenClicked;

    private Shape Kind => PlayerColor switch
    {
        "Red" => Shape.Pawn,
        "Green" => Shape.Diamond,
        "Yellow" => Shape.Star,
        _ => Shape.Orb
    };

    private Color GetColorCode()
    {
        return PlayerColor switch
        {
            "Red" => new Color(0.95f, 0.12f, 0.12f),
            "Green" => new Color(0.10f, 0.65f, 0.20f),
            "Yellow" => new Color(1.00f, 0.80f, 0.05f),
            "Blue" => new Color(0.10f, 0.50f, 0.95f),
            _ => Colors.White
        };
    }

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

        Color body = GetColorCode();
        Color dark = body.Darkened(0.45f);
        Color light = body.Lightened(0.35f);

        // Dashed ring on the ground around movable tokens
        if (_highlighted)
        {
            DrawDashedRing(r * 1.55f, Colors.Black, 6f * k);
            DrawDashedRing(r * 1.55f, Colors.White, 3.5f * k);
        }

        // Ground shadow (shrinks while the token is in the air)
        float shrink = 1f - Mathf.Clamp(_lift / (r * 1.6f), 0f, 0.35f);
        DrawColoredPolygon(
            Ellipse(new Vector2(0, r * 0.98f), r * 0.95f * shrink, r * 0.26f * shrink),
            new Color(0, 0, 0, 0.35f));

        // Everything below is lifted together while hopping
        DrawSetTransform(new Vector2(0, -_lift), 0f, Vector2.One);

        switch (Kind)
        {
            case Shape.Pawn: DrawPawn(r, k, body, dark, light); break;
            case Shape.Diamond: DrawDiamond(r, k, body, dark, light); break;
            case Shape.Star: DrawStarToken(r, k, body, dark, light); break;
            default: DrawOrb(r, k, body, dark, light); break;
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // Red: round head, tapered body, wide foot
    private void DrawPawn(float r, float k, Color body, Color dark, Color light)
    {
        // Foot
        Vector2[] foot = Ellipse(new Vector2(0, r * 0.92f), r * 0.88f, r * 0.24f);
        DrawColoredPolygon(foot, dark);
        DrawPolyline(Close(foot), Colors.Black, 2.2f * k, true);

        // Tapered body
        Vector2[] torso = BuildBody(r);
        DrawColoredPolygon(torso, body);
        DrawPolyline(Close(torso), Colors.Black, 2.2f * k, true);

        // Collar
        Vector2[] collar = Ellipse(new Vector2(0, -r * 0.28f), r * 0.44f, r * 0.14f);
        DrawColoredPolygon(collar, light);
        DrawPolyline(Close(collar), Colors.Black, 1.8f * k, true);

        // Head
        Vector2 head = new Vector2(0, -r * 0.72f);
        DrawCircle(head, r * 0.56f, light);
        DrawArc(head, r * 0.56f, 0f, Mathf.Tau, 32, Colors.Black, 2.2f * k, true);

        // Shine
        DrawColoredPolygon(
            Ellipse(head + new Vector2(-r * 0.18f, -r * 0.20f), r * 0.17f, r * 0.11f),
            new Color(1, 1, 1, 0.55f));
    }

    // Green: a cut gem with light and dark facets
    private void DrawDiamond(float r, float k, Color body, Color dark, Color light)
    {
        Vector2 top = new Vector2(0, -r * 1.15f);
        Vector2 right = new Vector2(r * 0.95f, -r * 0.10f);
        Vector2 bottom = new Vector2(0, r * 1.00f);
        Vector2 left = new Vector2(-r * 0.95f, -r * 0.10f);
        Vector2 mid = new Vector2(0, -r * 0.10f);

        DrawColoredPolygon(new[] { top, right, bottom, left }, body);
        DrawColoredPolygon(new[] { top, left, mid }, light);     // upper-left facet
        DrawColoredPolygon(new[] { right, bottom, mid }, dark);  // lower-right facet

        DrawLine(left, right, new Color(0, 0, 0, 0.35f), 1.5f * k, true);
        DrawPolyline(new[] { top, right, bottom, left, top }, Colors.Black, 2.4f * k, true);

        // Shine
        DrawColoredPolygon(
            Ellipse(new Vector2(-r * 0.28f, -r * 0.55f), r * 0.14f, r * 0.09f),
            new Color(1, 1, 1, 0.6f));
    }

    // Yellow: a five-point star
    private void DrawStarToken(float r, float k, Color body, Color dark, Color light)
    {
        Vector2 center = new Vector2(0, -r * 0.05f);
        var points = new Vector2[10];

        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.Pi * i / 5f - Mathf.Pi / 2f;
            float radius = i % 2 == 0 ? r * 1.22f : r * 0.55f;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        DrawColoredPolygon(points, body);

        // Lighter smaller star in the middle
        var inner = new Vector2[10];
        for (int i = 0; i < 10; i++)
            inner[i] = center + (points[i] - center) * 0.5f;
        DrawColoredPolygon(inner, new Color(light.R, light.G, light.B, 0.75f));

        DrawPolyline(Close(points), Colors.Black, 2.4f * k, true);
    }

    // Blue: a solid glossy ball with a bright core (clearly not a hollow yard socket)
    private void DrawOrb(float r, float k, Color body, Color dark, Color light)
    {
        float radius = r * 1.0f;

        DrawCircle(Vector2.Zero, radius, dark);
        DrawCircle(new Vector2(0, -radius * 0.06f), radius * 0.9f, body);
        DrawCircle(new Vector2(0, -radius * 0.06f), radius * 0.5f, light);
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 32, Colors.Black, 2.6f * k, true);
        DrawArc(new Vector2(0, -radius * 0.06f), radius * 0.5f, 0f, Mathf.Tau, 24,
            new Color(0, 0, 0, 0.3f), 1.4f * k, true);

        // Shine
        DrawColoredPolygon(
            Ellipse(new Vector2(-radius * 0.38f, -radius * 0.50f), radius * 0.20f, radius * 0.12f),
            new Color(1, 1, 1, 0.65f));
    }

    private Vector2[] BuildBody(float r)
    {
        const int n = 14;
        var pts = new List<Vector2>();

        // Right side from the foot up to the neck: wide at the bottom, narrow at the top
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float y = Mathf.Lerp(0.92f, -0.30f, t) * r;
            float halfWidth = (0.26f + 0.60f * Mathf.Pow(1f - t, 2.2f)) * r;
            pts.Add(new Vector2(halfWidth, y));
        }

        // Mirror back down the left side
        for (int i = n; i >= 0; i--)
            pts.Add(new Vector2(-pts[i].X, pts[i].Y));

        return pts.ToArray();
    }

    private static Vector2[] Ellipse(Vector2 center, float rx, float ry)
    {
        const int n = 24;
        var pts = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }

        return pts;
    }

    private static Vector2[] Close(Vector2[] pts)
    {
        var closed = new Vector2[pts.Length + 1];
        pts.CopyTo(closed, 0);
        closed[pts.Length] = pts[0];
        return closed;
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
