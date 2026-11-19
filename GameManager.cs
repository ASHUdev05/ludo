using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Coordinates a game: setup, turn flow, dice, moves, ranks, pause and rematch.
// The details live in smaller classes:
//   GameRules (rules), BotBrain (bot choices), TokenLayout (where tokens rest),
//   GameHud (dice and labels), WinScreen, PauseMenu, SettingsMenu, Sparkles, SoundManager.
public partial class GameManager : Node
{
    private enum State { Setup, WaitingForRoll, Rolling, WaitingForMove, Busy, GameOver }

    // Optional: assign in the inspector. If empty, it is found automatically.
    [Export] public BoardManager Board;

    [Export] public float DiceSize = 180f;
    [Export] public float DiceGap = 28f;

    // Capturing an opponent's token gives the capturer another turn
    [Export] public bool ExtraTurnOnCapture = true;

    // Bringing a token all the way home gives another turn
    [Export] public bool ExtraTurnOnHome = true;

    // Three sixes in a row: the third is void, the turn is lost, and the moves
    // made with the first two sixes are undone
    [Export] public bool ThreeSixesLoseTurn = true;

    // Chance of a 6 relative to each other face (1.0 = fair die, 2.0 = about 29% sixes)
    [Export] public float SixWeight = 2.0f;

    // Pacing (seconds)
    [Export] public float BotRollDelay = 0.45f;
    [Export] public float BotMoveDelay = 0.40f;
    [Export] public float HopDuration = 0.10f;

    private GameConfig _config;
    private GameRules _rules;
    private BotBrain _bot;
    private TokenLayout _layout;
    private GameHud _hud;
    private SoundManager _sound;
    private PauseMenu _pauseMenu;

    private readonly List<string> _activeOrder = new();
    private readonly HashSet<string> _auto = new(); // human seats currently played by the bot
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<Token> _movable = new();

    // Players who have brought all four tokens home: color -> rank (1 = first)
    private readonly Dictionary<string, int> _ranks = new();

    // Token positions before the first six of the current streak (for revoking)
    private Dictionary<Token, int> _sixSnapshot;

    private int _currentIndex = 0;
    private int _diceValue = 0;
    private int _sixStreak = 0;
    private State _state = State.Setup;

    // Bumped when this node leaves the tree, so delayed callbacks from an old game do nothing
    private int _epoch = 0;

    private string CurrentColor => _activeOrder[_currentIndex];

    private bool IsBot(string color) =>
        _config.Seats[color] == SeatType.Bot || _auto.Contains(color);

    private bool IsPlaying =>
        _hud != null && _state != State.Setup && _state != State.GameOver;

    public override void _Ready()
    {
        GameSettings.Load();
        GameSettings.Apply();

        _rng.Randomize();

        _sound = new SoundManager();
        AddChild(_sound);

        // Wait one frame so BoardManager has finished spawning tokens
        CallDeferred(nameof(Init));
    }

    public override void _ExitTree()
    {
        _epoch++;
    }

    // Runs an action after a delay. It pauses with the game and is ignored if the game
    // has been left or restarted in the meantime.
    private void Delay(double seconds, Action action)
    {
        int epoch = _epoch;

        GetTree().CreateTimer(seconds, false).Timeout += () =>
        {
            if (epoch != _epoch || !IsInstanceValid(this)) return;
            action();
        };
    }

    private void Init()
    {
        Board ??= FindBoard(GetTree().CurrentScene);

        if (Board == null)
        {
            GD.PushError("GameManager: BoardManager not found. Assign it in the inspector.");
            return;
        }

        // Rematch: skip the setup screen and reuse the last settings
        if (GameConfig.SkipSetup && GameConfig.Last != null)
        {
            GameConfig.SkipSetup = false;
            StartGame(GameConfig.Last.Clone());
            return;
        }

        var screen = new StartScreen { Config = GameConfig.Last?.Clone() ?? new GameConfig() };
        AddChild(screen);

        screen.Started += config =>
        {
            screen.QueueFree();
            StartGame(config);
        };
    }

    private static BoardManager FindBoard(Node node)
    {
        if (node == null) return null;
        if (node is BoardManager bm) return bm;

        foreach (Node child in node.GetChildren())
        {
            BoardManager found = FindBoard(child);
            if (found != null) return found;
        }
        return null;
    }

    // =====================================
    // GAME SETUP
    // =====================================

    private void StartGame(GameConfig config)
    {
        _config = config;
        GameConfig.Last = config.Clone();

        _activeOrder.Clear();
        _activeOrder.AddRange(config.ActiveColors);

        // Remove tokens (and grey out the yard) of seats that are not playing
        foreach (string color in GameConfig.Order)
            if (config.Seats[color] == SeatType.Off)
                Board.RemoveColor(color);

        _rules = new GameRules(Board, _config);
        _bot = new BotBrain(Board, _config, _rng);
        _layout = new TokenLayout(Board);

        _hud = new GameHud();
        AddChild(_hud);
        _hud.Build(Board, _config, _activeOrder, DiceSize, DiceGap);
        _hud.DiceClicked += OnDiceClicked;
        _hud.RollFinished += OnRollFinished;
        _hud.AutoToggled += SetAutoplay;
        _hud.PausePressed += TogglePause;

        foreach (string color in _activeOrder)
            UpdateTag(color);

        foreach (Token token in Board.AllTokens)
            token.OnTokenClicked += OnTokenClicked;

        _ranks.Clear();
        _currentIndex = 0;
        StartTurn();
    }

    private void UpdateTag(string color)
    {
        string who = _config.Seats[color] == SeatType.Bot ? "Bot"
                   : _auto.Contains(color) ? "Auto"
                   : "Player";

        string team = _config.IsTeams
            ? (color == "Red" || color == "Yellow" ? " - Team 1" : " - Team 2")
            : "";

        string rank = _ranks.TryGetValue(color, out int r) ? $" - {GameRules.Ordinal(r)}" : "";

        _hud.SetTag(color, $"{color} ({who}){team}{rank}");
    }

    private void SetStatus(string text) => _hud.SetStatus(text, CurrentColor);

    // =====================================
    // PAUSE, REMATCH, MENU
    // =====================================

    public override void _Notification(int what)
    {
        // Android back button: pause instead of quitting
        if (what == NotificationWMGoBackRequest)
        {
            TogglePause();
        }
        // Switching to another app pauses the game
        else if (what == NotificationApplicationFocusOut)
        {
            if (IsPlaying && !GetTree().Paused) PauseGame();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            TogglePause();
    }

    private void TogglePause()
    {
        if (!IsPlaying) return;

        if (GetTree().Paused) ResumeGame();
        else PauseGame();
    }

    private void PauseGame()
    {
        if (_pauseMenu != null) return;

        GetTree().Paused = true;

        _pauseMenu = new PauseMenu();
        _pauseMenu.ResumePressed += ResumeGame;
        _pauseMenu.RestartPressed += Rematch;
        _pauseMenu.MenuPressed += GoToMenu;
        AddChild(_pauseMenu);
    }

    private void ResumeGame()
    {
        GetTree().Paused = false;

        if (_pauseMenu != null)
        {
            _pauseMenu.QueueFree();
            _pauseMenu = null;
        }
    }

    // Same players and settings, fresh game, no setup screen
    private void Rematch()
    {
        GetTree().Paused = false;
        GameConfig.SkipSetup = true;
        GetTree().ReloadCurrentScene();
    }

    // Back to the setup screen
    private void GoToMenu()
    {
        GetTree().Paused = false;
        GameConfig.SkipSetup = false;
        GetTree().ReloadCurrentScene();
    }

    // =====================================
    // AUTOPLAY (bot plays for a human seat until switched off)
    // =====================================

    private void SetAutoplay(string color, bool on)
    {
        if (on) _auto.Add(color); else _auto.Remove(color);

        _hud.SetAutoText(color, on);
        UpdateTag(color);

        if (_state == State.GameOver || color != CurrentColor) return;

        if (_state == State.WaitingForRoll)
        {
            _hud.GetDice(color).CanRoll = !IsBot(color);

            if (on)
            {
                SetStatus($"{color} (auto) is thinking...");
                Delay(BotRollDelay, () => BotRollTick(color));
            }
            else
            {
                SetStatus($"{color}'s turn - tap your dice");
            }
        }
        else if (_state == State.WaitingForMove && on)
        {
            // The bot takes over a choice the human was still making
            _layout.Relayout();
            BotChooseAndMove();
        }
    }

    // =====================================
    // TURN FLOW
    // =====================================

    private void StartTurn()
    {
        _state = State.WaitingForRoll;

        string color = CurrentColor;
        DiceButton dice = _hud.GetDice(color);
        dice.IsTurn = true;

        if (IsBot(color))
        {
            dice.CanRoll = false; // humans can't roll for a bot
            SetStatus($"{color} (bot) is thinking...");
            Delay(BotRollDelay, () => BotRollTick(color));
        }
        else
        {
            dice.CanRoll = true;
            SetStatus($"{color}'s turn - tap your dice");
        }
    }

    private void BotRollTick(string color)
    {
        if (_state != State.WaitingForRoll || color != CurrentColor) return;

        // Auto was switched off while waiting: hand the dice back to the player
        if (!IsBot(color))
        {
            _hud.GetDice(color).CanRoll = true;
            SetStatus($"{color}'s turn - tap your dice");
            return;
        }

        OnDiceClicked(color);
    }

    private void EndTurn(bool extraTurn)
    {
        if (!extraTurn)
        {
            _hud.GetDice(CurrentColor).IsTurn = false;
            _sixStreak = 0;
            _sixSnapshot = null;

            // Skip players who already finished. In team games they stay in,
            // because they move their partner's tokens.
            do
            {
                _currentIndex = (_currentIndex + 1) % _activeOrder.Count;
            }
            while (!_config.IsTeams && _ranks.ContainsKey(CurrentColor));
        }

        StartTurn();
    }

    // Called by a human tapping their dice, or by the bot timer
    private void OnDiceClicked(string color)
    {
        if (_state != State.WaitingForRoll || color != CurrentColor) return;

        _state = State.Rolling;
        _diceValue = RollDie();
        _hud.GetDice(color).Roll(_diceValue);
        _sound.PlayDice();
        SetStatus($"{color} is rolling...");
    }

    // Faces 1-5 have weight 1; a 6 has weight SixWeight, so sixes come up more often
    private int RollDie()
    {
        float total = 5f + Mathf.Max(0f, SixWeight);
        float r = _rng.Randf() * total;

        return r >= 5f ? 6 : (int)r + 1;
    }

    // Called when the dice animation has settled on its value
    private void OnRollFinished(string color)
    {
        if (_state != State.Rolling || color != CurrentColor) return;

        // Track the streak. Remember the board before the first six so the whole
        // streak can be revoked if a third six follows.
        _sixStreak = _diceValue == 6 ? _sixStreak + 1 : 0;

        if (_sixStreak == 1)
            _sixSnapshot = Board.AllTokens.ToDictionary(t => t, t => t.StepsTaken);
        else if (_sixStreak == 0)
            _sixSnapshot = null; // streak broken: earlier moves are permanent

        if (ThreeSixesLoseTurn && _sixStreak >= 3)
        {
            _state = State.Busy;

            HashSet<Token> undone = RevokeSixMoves();
            SetStatus($"{CurrentColor} rolled three 6s - this streak's moves are undone!");
            GameSettings.Vibrate(120);

            Delay(1.0, () =>
            {
                foreach (Token t in undone) t.ZIndex = 0;
                EndTurn(false);
            });
            return;
        }

        _movable.Clear();
        _movable.AddRange(_rules.ControlledTokens(CurrentColor).Where(t => _rules.CanMove(t, _diceValue)));

        // Nothing can move: pass the turn
        if (_movable.Count == 0)
        {
            _state = State.Busy;
            SetStatus($"{CurrentColor} rolled {_diceValue} - no moves");
            Delay(0.7, () => EndTurn(false));
            return;
        }

        // Every movable token is in the same spot, so the outcome is identical:
        // move one automatically.
        bool allEquivalent = _movable
            .Select(t => (t.PlayerColor, t.StepsTaken))
            .Distinct()
            .Count() == 1;

        if (allEquivalent)
        {
            Token pick = _movable[_rng.RandiRange(0, _movable.Count - 1)];

            _state = State.Busy;
            SetStatus($"{CurrentColor} rolled {_diceValue}");
            Delay(0.25, () => MoveToken(pick));
            return;
        }

        // Bot chooses with its scoring
        if (IsBot(CurrentColor))
        {
            SetStatus($"{CurrentColor} (bot) rolled {_diceValue}");
            BotChooseAndMove();
            return;
        }

        // Human chooses: highlight the movable tokens and pop crowded ones out
        _state = State.WaitingForMove;
        foreach (Token t in _movable) t.Highlighted = true;
        _layout.PopOut(_movable);
        SetStatus($"{CurrentColor} rolled {_diceValue} - pick a token");
    }

    private void BotChooseAndMove()
    {
        Token pick = _bot.ChooseMove(_movable, _diceValue);

        _state = State.Busy;
        foreach (Token t in _movable) t.Highlighted = false;
        pick.Highlighted = true;

        Delay(BotMoveDelay, () => MoveToken(pick));
    }

    // Puts every token back where it was before this streak's sixes
    private HashSet<Token> RevokeSixMoves()
    {
        var changed = new HashSet<Token>();

        if (_sixSnapshot == null) return changed;

        foreach (var pair in _sixSnapshot)
        {
            if (!IsInstanceValid(pair.Key)) continue;
            if (pair.Key.StepsTaken == pair.Value) continue;

            pair.Key.StepsTaken = pair.Value;
            changed.Add(pair.Key);
        }

        _sixSnapshot = null;
        _layout.Relayout(changed); // they glide back to their old cells
        return changed;
    }

    // =====================================
    // MOVING TOKENS
    // =====================================

    private void OnTokenClicked(Token token)
    {
        if (_state != State.WaitingForMove) return;
        if (!_movable.Contains(token)) return;

        MoveToken(token);
    }

    private void MoveToken(Token token)
    {
        foreach (Token t in _movable) t.Highlighted = false;
        _state = State.Busy;

        // Cells to hop through
        var path = new List<Vector2>();
        int newSteps;

        if (token.IsInYard)
        {
            newSteps = 0;
            path.Add(Board.GetTokenLocalPosition(token.PlayerColor, 0));
        }
        else
        {
            newSteps = token.StepsTaken + _diceValue;
            for (int s = token.StepsTaken + 1; s <= newSteps; s++)
                path.Add(Board.GetTokenLocalPosition(token.PlayerColor, s));
        }

        token.StepsTaken = newSteps;

        // Moving token returns to full size and draws above the others
        token.ZIndex = 10;
        token.ScaleTo(1f, 0.15f);

        // Other tokens settle back into their normal layout (undoes any pop-out)
        _layout.Relayout(null, token);

        Tween tween = token.MoveAlongPath(path, HopDuration, _sound.PlayStep);
        tween.Finished += () => OnMoveFinished(token);
    }

    private void OnMoveFinished(Token token)
    {
        token.ZIndex = 0;

        // Landing on an unsafe cell sends every opposing token there back to its yard
        List<Token> captured = _rules.FindCaptures(token);
        foreach (Token victim in captured)
            victim.StepsTaken = -1;

        _layout.Relayout(new HashSet<Token>(captured));

        if (captured.Count > 0)
        {
            _sound.PlayCapture();
            GameSettings.Vibrate(60);
        }

        string victims = string.Join(", ", captured.Select(t => t.PlayerColor).Distinct());

        // Sparkles + sound when a token gets home; rank (and a bigger burst) when the whole house is done
        string tokenOwner = token.PlayerColor;
        bool reachedHome = token.IsFinished;
        bool houseDone = false;
        int houseRank = 0;

        if (reachedHome)
        {
            Sparkles.Burst(Board, token.GlobalPosition, 28);
            _sound.PlayStar();
            GameSettings.Vibrate(30);

            if (_rules.AllFinished(tokenOwner) && !_ranks.ContainsKey(tokenOwner))
            {
                houseDone = true;
                houseRank = AssignRank(tokenOwner);
            }
        }

        if (CheckGameOver()) return;

        // Extra turn for a 6, a capture, or bringing a token home
        bool extraTurn = _diceValue == 6
            || (captured.Count > 0 && ExtraTurnOnCapture)
            || (reachedHome && ExtraTurnOnHome);

        // A finished player in a free-for-all has nothing left to move
        if (houseDone && !_config.IsTeams) extraTurn = false;

        if (captured.Count > 0 || reachedHome)
        {
            // Pause briefly so the captures / sparkles can be seen
            _state = State.Busy;

            var parts = new List<string>();
            if (captured.Count > 0) parts.Add($"{CurrentColor} captured {victims}!");
            if (houseDone) parts.Add($"{tokenOwner} finished {GameRules.Ordinal(houseRank)}!");
            else if (reachedHome) parts.Add($"{tokenOwner} got a token home!");
            SetStatus(string.Join("  ", parts));

            Delay(0.6, () =>
            {
                foreach (Token victim in captured) victim.ZIndex = 0;
                EndTurn(extraTurn);
            });
            return;
        }

        EndTurn(extraTurn);
    }

    // =====================================
    // RANKS AND GAME OVER
    // =====================================

    // Called when a player's fourth token reaches home
    private int AssignRank(string color)
    {
        int rank = _ranks.Count + 1;
        _ranks[color] = rank;

        // Ranks are permanent, so an unfinished six-streak can no longer be revoked
        _sixSnapshot = null;

        // The house is replaced by a rank medal
        Board.SetRank(color, rank);
        Sparkles.Burst(Board, Board.ToGlobal(Board.GetYardCenter(color)), 70);

        UpdateTag(color);

        // Free-for-all: this player is out of the rotation
        if (!_config.IsTeams)
            _hud.HidePlayer(color);

        return rank;
    }

    // Free-for-all: ends when only one player is left. Teams: ends when a team is all home.
    private bool CheckGameOver()
    {
        string title;
        string colorName;

        if (_config.IsTeams)
        {
            if (!_rules.HasWon(CurrentColor)) return false;

            title = _rules.WinTitle(CurrentColor);
            colorName = CurrentColor;
        }
        else
        {
            int unfinished = _activeOrder.Count - _ranks.Count;
            if (unfinished > 1) return false;

            // The last player left takes the last rank
            foreach (string c in _activeOrder)
                if (!_ranks.ContainsKey(c))
                    _ranks[c] = _ranks.Count + 1;

            string winner = _ranks.First(p => p.Value == 1).Key;
            title = $"{winner} wins!";
            colorName = winner;
        }

        _state = State.GameOver;
        _hud.DisableAll();
        SetStatus(title);

        // Short pause so the final hop and sparkles are visible before the overlay
        Delay(0.6, () => ShowWinScreen(title, colorName));
        return true;
    }

    private void ShowWinScreen(string title, string colorName)
    {
        var screen = new WinScreen
        {
            TitleText = title,
            ColorName = colorName,
            Standings = _ranks.OrderBy(p => p.Value).Select(p => (p.Key, p.Value)).ToList()
        };

        screen.RematchPressed += Rematch;
        screen.MenuPressed += GoToMenu;
        AddChild(screen);
    }
}
