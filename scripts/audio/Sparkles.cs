using Godot;

// One-shot burst of glowing gold sparks. Textures are built once and reused.
public static class Sparkles
{
    private static GradientTexture2D _glow;
    private static Gradient _fade;

    public static void Burst(Node2D parent, Vector2 globalPosition, int amount)
    {
        if (_glow == null)
        {
            // Soft round glow used as the particle texture
            var glowGradient = new Gradient();
            glowGradient.SetColor(0, Colors.White);
            glowGradient.SetColor(1, new Color(1, 1, 1, 0));

            _glow = new GradientTexture2D
            {
                Gradient = glowGradient,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 32,
                Height = 32
            };

            // Gold that fades out over the particle's life
            _fade = new Gradient();
            _fade.SetColor(0, new Color(1f, 0.95f, 0.6f, 1f));
            _fade.SetColor(1, new Color(1f, 0.7f, 0.2f, 0f));
        }

        var sparkles = new CpuParticles2D
        {
            Emitting = false, // start after positioning
            Amount = amount,
            Lifetime = 1.0,
            OneShot = true,
            Explosiveness = 0.9f,
            Texture = _glow,
            Direction = Vector2.Up,
            Spread = 180f,
            Gravity = Vector2.Zero,
            InitialVelocityMin = 120f,
            InitialVelocityMax = 320f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.6f,
            ColorRamp = _fade,
            ZIndex = 50,
            Material = new CanvasItemMaterial
            {
                BlendMode = CanvasItemMaterial.BlendModeEnum.Add
            }
        };

        parent.AddChild(sparkles);
        sparkles.GlobalPosition = globalPosition;
        sparkles.Emitting = true;

        parent.GetTree().CreateTimer(2.0).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(sparkles))
                sparkles.QueueFree();
        };
    }
}
