using Godot;

// Full-screen settings overlay. Works from the start screen and from the pause menu.
// Laid out with anchors, so it is always centered whatever the window size is.
public partial class SettingsMenu : CanvasLayer
{
    // Adds a settings screen on top of everything under "parent"
    public static void Open(Node parent)
    {
        parent.AddChild(new SettingsMenu());
    }

    private Button _soundButton;
    private Button _vibrationButton;
    private Label _volumeLabel;

    public override void _Ready()
    {
        Layer = 40;
        ProcessMode = ProcessModeEnum.Always; // must keep working while the game is paused

        var dim = new ColorRect
        {
            Color = new Color(0.05f, 0.06f, 0.08f, 0.97f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(800, 0) };
        box.AddThemeConstantOverride("separation", 30);
        center.AddChild(box);

        box.AddChild(MakeLabel("Settings", 90, Colors.White));

        // Sound on / off
        _soundButton = new Button { CustomMinimumSize = new Vector2(800, 90) };
        _soundButton.AddThemeFontSizeOverride("font_size", 38);
        _soundButton.Pressed += () =>
        {
            GameSettings.SoundOn = !GameSettings.SoundOn;
            Changed();
        };
        box.AddChild(_soundButton);

        // Volume
        _volumeLabel = MakeLabel("", 34, new Color(0.85f, 0.85f, 0.85f));
        box.AddChild(_volumeLabel);

        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            Value = GameSettings.Volume * 100.0,
            CustomMinimumSize = new Vector2(800, 70)
        };
        slider.ValueChanged += value =>
        {
            GameSettings.Volume = (float)(value / 100.0);
            Changed();
        };
        box.AddChild(slider);

        // Vibration
        _vibrationButton = new Button { CustomMinimumSize = new Vector2(800, 90) };
        _vibrationButton.AddThemeFontSizeOverride("font_size", 38);
        _vibrationButton.Pressed += () =>
        {
            GameSettings.Vibration = !GameSettings.Vibration;
            Changed();
            GameSettings.Vibrate(60); // a little preview buzz when switched on
        };
        box.AddChild(_vibrationButton);

        // Game speed
        box.AddChild(MakeLabel("Game speed", 34, new Color(0.85f, 0.85f, 0.85f)));

        var speedRow = new HBoxContainer();
        speedRow.AddThemeConstantOverride("separation", 16);
        box.AddChild(speedRow);

        var group = new ButtonGroup();
        for (int i = 0; i < GameSettings.SpeedNames.Length; i++)
        {
            int index = i;

            var button = new Button
            {
                Text = GameSettings.SpeedNames[i],
                ToggleMode = true,
                ButtonGroup = group,
                CustomMinimumSize = new Vector2(250, 90),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            button.AddThemeFontSizeOverride("font_size", 34);
            button.SetPressedNoSignal(index == GameSettings.SpeedIndex);
            button.Toggled += on =>
            {
                if (!on) return;
                GameSettings.SpeedIndex = index;
                Changed();
            };
            speedRow.AddChild(button);
        }

        var done = new Button
        {
            Text = "Done",
            CustomMinimumSize = new Vector2(800, 100)
        };
        done.AddThemeFontSizeOverride("font_size", 44);
        done.Pressed += () =>
        {
            GameSettings.Save();
            QueueFree();
        };
        box.AddChild(done);

        Refresh();
    }

    private void Changed()
    {
        GameSettings.Apply();
        Refresh();
    }

    private void Refresh()
    {
        _soundButton.Text = GameSettings.SoundOn ? "Sound: ON" : "Sound: OFF";
        _vibrationButton.Text = GameSettings.Vibration ? "Vibration: ON" : "Vibration: OFF";
        _volumeLabel.Text = $"Volume: {Mathf.RoundToInt(GameSettings.Volume * 100f)}%";
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
}
