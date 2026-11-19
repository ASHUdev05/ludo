using Godot;
using System;

// Shown while the game is paused: Resume, Restart (rematch), Settings, Main menu.
public partial class PauseMenu : CanvasLayer
{
    public event Action ResumePressed;
    public event Action RestartPressed;
    public event Action MenuPressed;

    public override void _Ready()
    {
        Layer = 30;
        ProcessMode = ProcessModeEnum.Always; // must keep working while the game is paused

        Vector2 view = GetViewport().GetVisibleRect().Size;

        AddChild(new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
            Size = view,
            MouseFilter = Control.MouseFilterEnum.Stop
        });

        var box = new VBoxContainer
        {
            Position = new Vector2(view.X / 2f - 350f, view.Y / 2f - 420f),
            Size = new Vector2(700f, 840f),
            Alignment = BoxContainer.AlignmentMode.Center
        };
        box.AddThemeConstantOverride("separation", 34);
        AddChild(box);

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

    // Escape (or the back button routed as ui_cancel) resumes
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
