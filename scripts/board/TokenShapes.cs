using Godot;
using System.Collections.Generic;

// Draws the token shapes. Used by Token (the real pieces) and by BoardManager
// (the small marker in each house showing which shape a color uses):
//   Red = pawn, Green = diamond, Yellow = heart, Blue = orb
public static class TokenShapes
{
    public static Color BodyColor(string playerColor)
    {
        return playerColor switch
        {
            "Red" => new Color(0.95f, 0.12f, 0.12f),
            "Green" => new Color(0.10f, 0.65f, 0.20f),
            "Yellow" => new Color(1.00f, 0.80f, 0.05f),
            "Blue" => new Color(0.10f, 0.50f, 0.95f),
            _ => Colors.White
        };
    }

    // Draws the shape for a color, centered on "o". Must be called while "c" is drawing.
    public static void Draw(CanvasItem c, string playerColor, Vector2 o, float r)
    {
        float k = r / 22f;

        Color body = BodyColor(playerColor);
        Color dark = body.Darkened(0.45f);
        Color light = body.Lightened(0.35f);

        switch (playerColor)
        {
            case "Red": DrawPawn(c, o, r, k, body, dark, light); break;
            case "Green": DrawDiamond(c, o, r, k, body, dark, light); break;
            case "Yellow": DrawHeart(c, o, r, k, body, light); break;
            default: DrawOrb(c, o, r, k, body, dark, light); break;
        }
    }

    // Red: round head, tapered body, wide foot
    private static void DrawPawn(CanvasItem c, Vector2 o, float r, float k, Color body, Color dark, Color light)
    {
        // Foot
        Vector2[] foot = Ellipse(o + new Vector2(0, r * 0.92f), r * 0.88f, r * 0.24f);
        c.DrawColoredPolygon(foot, dark);
        c.DrawPolyline(Close(foot), Colors.Black, 2.2f * k, true);

        // Tapered body
        Vector2[] torso = Shift(BuildBody(r), o);
        c.DrawColoredPolygon(torso, body);
        c.DrawPolyline(Close(torso), Colors.Black, 2.2f * k, true);

        // Collar
        Vector2[] collar = Ellipse(o + new Vector2(0, -r * 0.28f), r * 0.44f, r * 0.14f);
        c.DrawColoredPolygon(collar, light);
        c.DrawPolyline(Close(collar), Colors.Black, 1.8f * k, true);

        // Head
        Vector2 head = o + new Vector2(0, -r * 0.72f);
        c.DrawCircle(head, r * 0.56f, light);
        c.DrawArc(head, r * 0.56f, 0f, Mathf.Tau, 32, Colors.Black, 2.2f * k, true);

        // Shine
        c.DrawColoredPolygon(
            Ellipse(head + new Vector2(-r * 0.18f, -r * 0.20f), r * 0.17f, r * 0.11f),
            new Color(1, 1, 1, 0.55f));
    }

    // Green: a cut gem with light and dark facets
    private static void DrawDiamond(CanvasItem c, Vector2 o, float r, float k, Color body, Color dark, Color light)
    {
        Vector2 top = o + new Vector2(0, -r * 1.15f);
        Vector2 right = o + new Vector2(r * 0.95f, -r * 0.10f);
        Vector2 bottom = o + new Vector2(0, r * 1.00f);
        Vector2 left = o + new Vector2(-r * 0.95f, -r * 0.10f);
        Vector2 mid = o + new Vector2(0, -r * 0.10f);

        c.DrawColoredPolygon(new[] { top, right, bottom, left }, body);
        c.DrawColoredPolygon(new[] { top, left, mid }, light);     // upper-left facet
        c.DrawColoredPolygon(new[] { right, bottom, mid }, dark);  // lower-right facet

        c.DrawLine(left, right, new Color(0, 0, 0, 0.35f), 1.5f * k, true);
        c.DrawPolyline(new[] { top, right, bottom, left, top }, Colors.Black, 2.4f * k, true);

        // Shine
        c.DrawColoredPolygon(
            Ellipse(o + new Vector2(-r * 0.28f, -r * 0.55f), r * 0.14f, r * 0.09f),
            new Color(1, 1, 1, 0.6f));
    }

    // Yellow: a heart (a different silhouette from the board's star squares)
    private static void DrawHeart(CanvasItem c, Vector2 o, float r, float k, Color body, Color light)
    {
        const int n = 40;
        float scale = r * 0.95f / 16f;

        // The classic heart curve. Its middle sits a bit below the origin, so lift it.
        Vector2 center = o + new Vector2(0, -r * 0.16f);
        var points = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            float t = Mathf.Tau * i / n;
            float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
            float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t)
                    - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);

            points[i] = center + new Vector2(x, -y) * scale;
        }

        c.DrawColoredPolygon(points, body);

        // Lighter smaller heart inside, nudged up
        var inner = new Vector2[n];
        for (int i = 0; i < n; i++)
            inner[i] = center + new Vector2(0, -r * 0.10f) + (points[i] - center) * 0.52f;
        c.DrawColoredPolygon(inner, new Color(light.R, light.G, light.B, 0.75f));

        c.DrawPolyline(Close(points), Colors.Black, 2.4f * k, true);

        // Shine on the upper-left lobe
        c.DrawColoredPolygon(
            Ellipse(center + new Vector2(-r * 0.55f, -r * 0.42f), r * 0.18f, r * 0.11f),
            new Color(1, 1, 1, 0.6f));
    }

    // Blue: a solid glossy ball with a bright core (clearly not a hollow yard socket)
    private static void DrawOrb(CanvasItem c, Vector2 o, float r, float k, Color body, Color dark, Color light)
    {
        float radius = r * 1.0f;
        Vector2 core = o + new Vector2(0, -radius * 0.06f);

        c.DrawCircle(o, radius, dark);
        c.DrawCircle(core, radius * 0.9f, body);
        c.DrawCircle(core, radius * 0.5f, light);
        c.DrawArc(o, radius, 0f, Mathf.Tau, 32, Colors.Black, 2.6f * k, true);
        c.DrawArc(core, radius * 0.5f, 0f, Mathf.Tau, 24, new Color(0, 0, 0, 0.3f), 1.4f * k, true);

        // Shine
        c.DrawColoredPolygon(
            Ellipse(o + new Vector2(-radius * 0.38f, -radius * 0.50f), radius * 0.20f, radius * 0.12f),
            new Color(1, 1, 1, 0.65f));
    }

    private static Vector2[] BuildBody(float r)
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

    public static Vector2[] Ellipse(Vector2 center, float rx, float ry)
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

    private static Vector2[] Shift(Vector2[] pts, Vector2 offset)
    {
        var moved = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++)
            moved[i] = pts[i] + offset;
        return moved;
    }
}