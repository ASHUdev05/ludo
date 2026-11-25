using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Coordinates a game: setup, turn flow, saved rolls, moves, ranks, pause and rematch.
// The details live in smaller classes:
//   GameRules (rules), BotBrain (bot choices), TokenLayout (where tokens rest),
//   GameHud (dice, saved-roll chips), WinScreen, PauseMenu, SettingsMenu, RulesScreen,
//   Sparkles, SoundManager.
//
// A TURN
//   1. Roll phase: you roll. A 6 is saved and you roll again; the first non-6 ends the rolling.
//      (A capture or a token reaching home earns one more roll after your saved rolls are spent.)
//   2. Move phase: you spend the saved rolls one at a time, in any order, on any tokens.
//      For example 6, 6 and 4 can be 16 cells on one token, or 10 on one and 6 on another.
public partial class GameManager : Node
{
    private enum State { Setup, WaitingForRoll, Rolling, WaitingForMove, Busy, GameOver }

    // Optional: assign in the inspector. If empty, it is found automatically.
    [Export] public BoardManager Board;

    [Export] public float DiceSize = 180f;
    [Export] public float DiceGap = 28f;

    // Capturing an opponent's token earns another roll
    [Export] public bool ExtraTurnOnCapture = true;

    // Bringing a token all the way home earns another roll
    [Export] public bool ExtraTurnOnHome = true;

    // On: a 6 is saved and you roll again, then spend all saved rolls in any order.
    // Off: every roll is played right away and a 6 just earns another roll afterwards.
    [Export] public bool CarryRolls = true;

    // Chance of a 6 relative to each other face (1.0 = fair die, 2.0 = about 29% sixes)
    [Export] public float SixWeight = 2.0f;

    // Show the small name labels next to each dice
    [Export] public bool ShowNameTags = true;

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

    // Tokens that can move with the roll currently being played (a pair counts once)
    private readonly List<Token> _movable = new();

    // Players who have brought all four tokens home: color -> rank (1 = first)
    private readonly Dictionary<string, int> _ranks = new();

    // Saved rolls waiting to be spent, which one is selected, and how many rolls are still owed
    private readonly List<int> _pending = new();
    private int _selected = 0;
    private int _rollsOwed = 0;

    private int _currentIndex = 0;
    private int _diceValue = 0;   // the roll being rolled, then the roll being played
    private int _sixStreak = 0;   // consecutive 6s in the current rolling streak
    private State _state = State.Setup;

    // Bumped when this node leaves the tree, so delayed callbacks from an old game do nothing
    private int _epoch = 0;

    // Set when the app goes to the background; the pause menu opens once it is back
    private bool _pauseWhenBack = false;

    // Three Sixes mode only
    private bool ThreeSixesActive => _config.Rules == RuleSet.ThreeSixes;

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

        _rules = new GameRules(Board, _config, _activeOrder);
        _bot = new BotBrain(Board, _config, _rules, _rng);
        _layout = new TokenLayout(Board);

        _hud = new GameHud { ShowTags = ShowNameTags };
        AddChild(_hud);
        _hud.Build(Board, _config, _activeOrder, DiceSize, DiceGap);
        _hud.DiceClicked += OnDiceClicked;
        _hud.RollFinished += OnRollFinished;
        _hud.AutoToggled += SetAutoplay;
        _hud.PausePressed += TogglePause;
        _hud.PendingChipClicked += OnChipClicked;

        foreach (string color in _activeOrder)
            UpdateTag(color);

        foreach (Token token in Board.AllTokens)
            token.OnTokenClicked += OnTokenClicked;

        // Deathmatch / Supermatch: every token starts out on its start square
        if (_config.AllTokensOut)
        {
            foreach (Token t in Board.AllTokens)
            {
                t.StepsTaken = 0;
                t.Position = Board.GetTokenLocalPosition(t.PlayerColor, 0);
            }

            _layout.Relayout();
        }

        RefreshTokenMarks();

        _ranks.Clear();
        _currentIndex = 0;
        _state = State.Setup;

        // Special modes explain their rules first; the game begins when the player taps "Let's play"
        if (_config.Rules != RuleSet.Classic)
        {
            var rulesScreen = new RulesScreen { Rules = _config.Rules };
            rulesScreen.Closed += StartTurn;
            AddChild(rulesScreen);
        }
        else
        {
            StartTurn();
        }
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

    // Updates what is drawn from token positions: pairs, Supermatch "x2 / x3 / x4" badges
    // and the frames around blockades
    private void RefreshTokenMarks()
    {
        _rules.NormalizePairs();

        foreach (Token t in Board.AllTokens)
            t.Multiplier = _rules.Multiplier(t);

        Board.SetBlockades(_rules.GetBlockades());
    }

    // =====================================
    // PAUSE, REMATCH, MENU
    // =====================================

    public override void _Notification(int what)
    {
        // Android back button: pause instead of quitting
        if (what == NotificationWMGoBackRequest)
        {
            // Deferred: notifications can arrive while this node is busy adding children
            if (IsInsideTree()) CallDeferred(nameof(TogglePause));
        }
        // Leaving the app (for example the screen turns off): remember to pause,
        // but open the menu only once the app is back and has its real size again
        else if (what == NotificationApplicationFocusOut)
        {
            if (IsInsideTree() && IsPlaying && !GetTree().Paused)
                _pauseWhenBack = true;
        }
        else if (what == NotificationApplicationFocusIn)
        {
            if (_pauseWhenBack)
            {
                _pauseWhenBack = false;

                if (IsInsideTree() && IsPlaying && !GetTree().Paused)
                    CallDeferred(nameof(PauseGame));
            }
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
        if (_pauseMenu != null || !IsPlaying) return;

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
                Delay(BotRollDelay, () => BotRollTick(color));
        }
        else if (_state == State.WaitingForMove && on)
        {
            // The bot takes over a choice the human was still making
            SetHighlights(false);
            _layout.Relayout();
            BotChooseAndMove();
        }
    }

    // =====================================
    // TURN FLOW
    // =====================================

    private void StartTurn()
    {
        _pending.Clear();
        _hud.ClearPending();
        _selected = 0;
        _rollsOwed = 1;
        _sixStreak = 0;

        AwaitRoll();
    }

    // Waits for the current player to roll (humans tap their dice, bots roll by themselves)
    private void AwaitRoll()
    {
        _state = State.WaitingForRoll;

        string color = CurrentColor;
        DiceButton dice = _hud.GetDice(color);
        dice.IsTurn = true;

        if (IsBot(color))
        {
            dice.CanRoll = false; // humans can't roll for a bot
            Delay(BotRollDelay, () => BotRollTick(color));
        }
        else
        {
            dice.CanRoll = true;
        }
    }

    private void BotRollTick(string color)
    {
        if (_state != State.WaitingForRoll || color != CurrentColor) return;

        // Auto was switched off while waiting: hand the dice back to the player
        if (!IsBot(color))
        {
            _hud.GetDice(color).CanRoll = true;
            return;
        }

        OnDiceClicked(color);
    }

    private void EndTurn()
    {
        _hud.GetDice(CurrentColor).IsTurn = false;
        _pending.Clear();
        _hud.ClearPending();

        // Skip players who already finished. In team games they stay in,
        // because they move their partner's tokens.
        do
        {
            _currentIndex = (_currentIndex + 1) % _activeOrder.Count;
        }
        while (!_config.IsTeams && _ranks.ContainsKey(CurrentColor));

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

        // Three Sixes mode: three 6s in a row lose the whole streak and the rest of the turn
        if (ThreeSixesActive)
        {
            _sixStreak = _diceValue == 6 ? _sixStreak + 1 : 0;

            if (_sixStreak >= 3)
            {
                _state = State.Busy;
                _pending.Clear();
                _hud.ClearPending();
                _hud.Toast("Three 6s! Streak lost", CurrentColor);
                GameSettings.Vibrate(120);

                Delay(1.0, EndTurn);
                return;
            }
        }

        // A 6 that no token can play (for example everything left is in the home lane) is void:
        // it is not saved and it does not earn another roll
        if (CarryRolls && _diceValue == 6 && !HasUsableToken(6))
        {
            _state = State.Busy;
            _rollsOwed--;
            _selected = 0;

            if (_pending.Count == 0)
            {
                _hud.Toast("No moves", CurrentColor);
                Delay(0.7, FinishMovePhase);
            }
            else
            {
                // Rolls saved earlier can still be played
                ContinueMovePhase();
            }

            return;
        }

        // Save the roll
        _pending.Add(_diceValue);

        // A 6 is saved and you roll again straight away (it does not use up your roll)
        if (CarryRolls && _diceValue == 6)
        {
            RefreshPendingChips();
            AwaitRoll();
            return;
        }

        _rollsOwed--;
        _selected = 0;
        ContinueMovePhase();
    }

    // =====================================
    // MOVE PHASE: spend the saved rolls
    // =====================================

    // Picks the roll to play, then lets the player (or the bot) choose a token for it
    private void ContinueMovePhase()
    {
        // Keep the chosen roll if it can still be played, otherwise take the first playable one
        if (_selected < 0 || _selected >= _pending.Count || !HasUsableToken(_pending[_selected]))
            _selected = FirstUsableIndex();

        if (_selected < 0)
        {
            // Nothing that is left can be played
            _state = State.Busy;

            bool hadRolls = _pending.Count > 0;
            _pending.Clear();
            _hud.ClearPending();

            if (hadRolls) _hud.Toast("No moves", CurrentColor);

            Delay(hadRolls ? 0.7 : 0.05, FinishMovePhase);
            return;
        }

        _diceValue = _pending[_selected];
        _movable.Clear();
        _movable.AddRange(BuildMovable(_diceValue));
        RefreshPendingChips();

        // If only one token (or a group of identical tokens, such as several in the yard) can move
        // with any of the saved rolls, there is nothing to choose: play them in the saved order
        var movableGroups = new HashSet<(string Color, int Steps)>();

        foreach (int value in _pending.Distinct())
            foreach (Token t in BuildMovable(value))
                movableGroups.Add((t.PlayerColor, t.StepsTaken));

        if (movableGroups.Count == 1 && _movable.Count > 0)
        {
            Token pick = _movable[_rng.RandiRange(0, _movable.Count - 1)];

            _state = State.Busy;
            Delay(0.25, () => MoveToken(pick));
            return;
        }

        if (IsBot(CurrentColor))
        {
            BotChooseAndMove();
            return;
        }

        // Human chooses: highlight the movable tokens and pop crowded ones out
        _state = State.WaitingForMove;
        SetHighlights(true);
        _layout.PopOut(WithPartners(_movable));
    }

    // After the last saved roll is spent: roll again if a roll is owed, otherwise end the turn
    private void FinishMovePhase()
    {
        _hud.ClearPending();

        if (_rollsOwed > 0) AwaitRoll();
        else EndTurn();
    }

    private void AfterMove()
    {
        if (_pending.Count > 0)
        {
            _selected = 0;
            ContinueMovePhase();
        }
        else
        {
            FinishMovePhase();
        }
    }

    private bool HasUsableToken(int value)
    {
        return _rules.ControlledTokens(CurrentColor).Any(t => _rules.CanMove(t, value));
    }

    private int FirstUsableIndex()
    {
        for (int i = 0; i < _pending.Count; i++)
            if (HasUsableToken(_pending[i]))
                return i;

        return -1;
    }

    // Tokens that can move by this roll. A pair moves as one piece, so it counts once.
    private List<Token> BuildMovable(int value)
    {
        var list = new List<Token>();

        foreach (Token t in _rules.ControlledTokens(CurrentColor).Where(t => _rules.CanMove(t, value)))
        {
            if (t.PairedWith != null && list.Contains(t.PairedWith)) continue;

            list.Add(t);
        }

        return list;
    }

    // Shows the saved rolls in the middle of the board when there are two or more
    private void RefreshPendingChips()
    {
        if (_pending.Count < 2)
        {
            _hud.ClearPending();
            return;
        }

        var usable = _pending.Select(v => HasUsableToken(v)).ToList();
        _hud.ShowPending(_pending, _selected, usable, CurrentColor);
    }

    // The player tapped one of the saved rolls: play that one next
    private void OnChipClicked(int index)
    {
        if (_state != State.WaitingForMove) return;
        if (index < 0 || index >= _pending.Count || index == _selected) return;
        if (!HasUsableToken(_pending[index])) return;

        // Undo the highlights and pop-out made for the old roll
        SetHighlights(false);
        _layout.Relayout();

        _selected = index;
        _diceValue = _pending[index];
        _movable.Clear();
        _movable.AddRange(BuildMovable(_diceValue));
        RefreshPendingChips();

        SetHighlights(true);
        _layout.PopOut(WithPartners(_movable));
    }

    // Highlights (or clears) every movable token, including the partner of a pair
    private void SetHighlights(bool on)
    {
        foreach (Token t in _movable)
        {
            t.Highlighted = on;

            if (t.PairedWith != null)
                t.PairedWith.Highlighted = on;
        }
    }

    // The movable tokens plus the partners they would take along
    private List<Token> WithPartners(List<Token> tokens)
    {
        var all = new List<Token>(tokens);

        foreach (Token t in tokens)
            if (t.PairedWith != null && !all.Contains(t.PairedWith))
                all.Add(t.PairedWith);

        return all;
    }

    // The bot looks at every saved roll and every token it could move, and plays the best pair
    private void BotChooseAndMove()
    {
        Token best = null;
        int bestValue = _diceValue;
        float bestScore = float.MinValue;

        foreach (int value in _pending.Distinct())
        {
            foreach (Token t in BuildMovable(value))
            {
                // Small random term breaks ties so bots aren't perfectly predictable
                float score = _bot.Score(t, value) + _rng.Randf() * 3f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                    bestValue = value;
                }
            }
        }

        if (best == null)
        {
            // Nothing playable (should not happen): give up the rest of the rolls
            _pending.Clear();
            FinishMovePhase();
            return;
        }

        _selected = _pending.IndexOf(bestValue);
        _diceValue = bestValue;
        _movable.Clear();
        _movable.AddRange(BuildMovable(bestValue));
        RefreshPendingChips();

        _state = State.Busy;
        SetHighlights(false);
        best.Highlighted = true;

        if (best.PairedWith != null)
            best.PairedWith.Highlighted = true;

        Token pick = best;
        Delay(BotMoveDelay, () => MoveToken(pick));
    }

    // =====================================
    // MOVING TOKENS
    // =====================================

    private void OnTokenClicked(Token token)
    {
        if (_state != State.WaitingForMove) return;

        // Tapping either token of a pair chooses the pair
        Token chosen = _movable.Contains(token) ? token
            : (token.PairedWith != null && _movable.Contains(token.PairedWith)) ? token.PairedWith
            : null;

        if (chosen == null) return;

        MoveToken(chosen);
    }

    private void MoveToken(Token token)
    {
        SetHighlights(false);
        _state = State.Busy;

        // The roll being played is spent
        if (_selected >= 0 && _selected < _pending.Count)
            _pending.RemoveAt(_selected);

        RefreshPendingChips();

        // A paired token moves together with its partner
        Token partner = token.PairedWith;

        // Work everything out before any token is changed
        int newSteps = _rules.TargetSteps(token, _diceValue);
        int partnerSteps = partner != null ? _rules.TargetSteps(partner, _diceValue) : 0;
        bool pairBreaks = partner != null && _rules.PairBreaks(token, _diceValue);

        List<Vector2> path = BuildPath(token, newSteps);
        List<Vector2> partnerPath = partner != null ? BuildPath(partner, partnerSteps) : null;

        token.StepsTaken = newSteps;
        if (partner != null) partner.StepsTaken = partnerSteps;

        // Moving tokens return to full size and draw above the others
        token.ZIndex = 10;
        token.ScaleTo(1f, 0.15f);

        if (partner != null)
        {
            partner.ZIndex = 10;
            partner.ScaleTo(1f, 0.15f);
        }

        // Other tokens settle back into their normal layout (undoes any pop-out)
        _layout.Relayout(null, token, partner);

        float stepTime = Mathf.Min(HopDuration, 0.8f / Mathf.Max(1, path.Count));
        Tween tween = token.MoveAlongPath(path, stepTime, _sound.PlayStep);
        partner?.MoveAlongPath(partnerPath, stepTime);

        tween.Finished += () => OnMoveFinished(token, partner, pairBreaks);
    }

    // Cells a token hops through to reach targetSteps (Supermatch moves can be long)
    private List<Vector2> BuildPath(Token t, int targetSteps)
    {
        var path = new List<Vector2>();

        if (t.IsInYard)
        {
            path.Add(Board.GetTokenLocalPosition(t.PlayerColor, 0));
            return path;
        }

        for (int s = t.StepsTaken + 1; s <= targetSteps; s++)
            path.Add(Board.GetTokenLocalPosition(t.PlayerColor, s));

        return path;
    }

    private void OnMoveFinished(Token token, Token partner, bool pairBreaks)
    {
        token.ZIndex = 0;
        if (partner != null) partner.ZIndex = 0;

        bool pairMove = partner != null;
        bool joinedPair = false;

        if (pairMove)
        {
            // A pair splits when it reaches a star, a start square or the home lane
            if (pairBreaks) _rules.Unpair(token);
        }
        else
        {
            // A single token that lands on a teammate's cell forms a pair with it
            joinedPair = _rules.TryFormPair(token);
        }

        // A single token landing on an unsafe cell sends every unprotected opposing token there
        // back to its yard. A pair that moved captures opposing pairs only. A token that has just
        // joined a teammate captures nothing. Stars and start squares are always safe.
        List<Token> captured = joinedPair
            ? new List<Token>()
            : _rules.FindCaptures(token, pairMove);

        foreach (Token victim in captured)
            victim.StepsTaken = -1;

        _layout.Relayout(new HashSet<Token>(captured));
        RefreshTokenMarks();

        if (captured.Count > 0)
        {
            _sound.PlayCapture();
            GameSettings.Vibrate(60);
        }

        // Sparkles + sound when a token gets home; rank (and a bigger burst) when the whole house is done
        string tokenOwner = token.PlayerColor;
        bool reachedHome = token.IsFinished;
        bool houseDone = false;

        if (reachedHome)
        {
            Sparkles.Burst(Board, token.GlobalPosition, 28);
            _sound.PlayStar();
            GameSettings.Vibrate(30);

            if (_rules.AllFinished(tokenOwner) && !_ranks.ContainsKey(tokenOwner))
            {
                houseDone = true;
                AssignRank(tokenOwner);
            }
        }

        if (CheckGameOver()) return;

        // Bonus rolls, taken once the saved rolls are spent
        if (captured.Count > 0 && ExtraTurnOnCapture) _rollsOwed++;
        if (reachedHome && ExtraTurnOnHome) _rollsOwed++;
        if (!CarryRolls && _diceValue == 6) _rollsOwed++;

        // A finished player in a free-for-all has nothing left to move
        if (houseDone && !_config.IsTeams)
        {
            _pending.Clear();
            _hud.ClearPending();
            _rollsOwed = 0;
        }

        if (captured.Count > 0 || reachedHome)
        {
            // Pause briefly so the captures / sparkles can be seen
            _state = State.Busy;

            Delay(0.6, () =>
            {
                foreach (Token victim in captured) victim.ZIndex = 0;
                AfterMove();
            });
            return;
        }

        AfterMove();
    }

    // =====================================
    // RANKS AND GAME OVER
    // =====================================

    // Called when a player's fourth token reaches home
    private void AssignRank(string color)
    {
        int rank = _ranks.Count + 1;
        _ranks[color] = rank;

        // The house is replaced by a rank medal
        Board.SetRank(color, rank);
        Sparkles.Burst(Board, Board.ToGlobal(Board.GetYardCenter(color)), 70);

        UpdateTag(color);

        // Free-for-all: this player is out of the rotation
        if (!_config.IsTeams)
            _hud.HidePlayer(color);
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
        _hud.ClearPending();

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
