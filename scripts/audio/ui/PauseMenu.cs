using Godot;
using System;

// Shown while the game is paused: Resume, Restart (rematch), Settings, Main menu.
// Laid out with anchors, so it is always centered whatever the window size is.
public partial class PauseMenu : CanvasLayer
{
    public event Action ResumePressed;
    public event Action RestartPressed;
    public event Action MenuPressed;

    public override void _Ready()
    {
        Layer = 30;
        ProcessMode = ProcessModeEnum.Always; // must keep working while the game is paused

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(700, 0) };
        box.AddThemeConstantOverride("separation", 34);
        center.AddChild(box);

        var title = new Label
        {
            Text = "Paused",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 100);
        title.AddThemeColorOverride("font_color", Colors.White);
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 14);
        box.AddChild(title);

        box.AddChild(MakeButton("Resume", () => ResumePressed?.Invoke()));
        box.AddChild(MakeButton("Restart", () => RestartPressed?.Invoke()));
        box.AddChild(MakeButton("Settings", () => SettingsMenu.Open(this)));
        box.AddChild(MakeButton("Main menu", () => MenuPressed?.Invoke()));
    }

    // Escape resumes
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            ResumePressed?.Invoke();
        }
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(700, 110)
        };
        button.AddThemeFontSizeOverride("font_size", 46);
        button.Pressed += () => onPressed();
        return button;
    }
}
