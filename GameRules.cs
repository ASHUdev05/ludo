using System;
using System.Collections.Generic;
using System.Linq;

// The rules of Ludo, kept separate from UI and flow so they are easy to read and test.
public class GameRules
{
    // 0..50 track, 51..55 home lane, 56 = finished
    public const int HomeSteps = 56;

    private readonly BoardManager _board;
    private readonly GameConfig _config;
    private readonly IReadOnlyList<string> _activeColors;

    public GameRules(BoardManager board, GameConfig config, IReadOnlyList<string> activeColors)
    {
        _board = board;
        _config = config;
        _activeColors = activeColors;
    }

    private bool IsSupermatch => _config.Rules == RuleSet.Supermatch;

    // Supermatch: a token's speed multiplier. It starts at x1 and goes up by one each time
    // the token reaches another player's start square (x2, x3, x4...). Always x1 otherwise.
    public int Multiplier(Token t)
    {
        if (!IsSupermatch || t.IsInYard) return 1;

        int loop = _board.MainPathPositions.Count;
        int myStart = _board.GetStartIndex(t.PlayerColor);
        int multiplier = 1;

        foreach (string other in _activeColors)
        {
            if (other == t.PlayerColor) continue;

            // How many steps along its own path this token reaches that player's start square
            int offset = (_board.GetStartIndex(other) - myStart + loop) % loop;

            if (t.StepsTaken >= offset)
                multiplier++;
        }

        return multiplier;
    }

    public bool CanMove(Token t, int dice)
    {
        if (t.IsInYard) return dice == 6;               // need a 6 to leave the yard
        if (t.IsFinished) return false;

        // Can't overshoot home (Supermatch just finishes at home instead)
        if (!IsSupermatch && t.StepsTaken + dice > HomeSteps) return false;

        // Can't land on or pass through an opposing blockade
        return !PathBlocked(t, TargetSteps(t, dice));
    }

    // =====================================
    // BLOCKADES
    // =====================================
    // Two or more tokens of the same team on one cell form a blockade: opponents can't land on
    // it or pass through it, and its tokens can't be captured. Stars and start squares are
    // shared safe cells, so they never hold blockades.

    private string TeamKey(string color)
    {
        if (!_config.IsTeams) return color;
        return color == "Red" || color == "Yellow" ? "Team1" : "Team2";
    }

    // Does the shared-track cell hold a blockade that stops a token of "moverColor"?
    public bool IsBlockedCell(string moverColor, int pathIndex)
    {
        if (pathIndex < 0 || _board.IsSafeIndex(pathIndex)) return false;

        var counts = new Dictionary<string, int>();

        foreach (Token t in _board.AllTokens)
        {
            if (_board.GetPathIndex(t.PlayerColor, t.StepsTaken) != pathIndex) continue;
            if (_config.SameTeam(moverColor, t.PlayerColor)) continue; // your own team never blocks you

            string team = TeamKey(t.PlayerColor);
            counts[team] = counts.GetValueOrDefault(team) + 1;
        }

        return counts.Values.Any(count => count >= 2);
    }

    // True if any cell the token would enter on its way to targetSteps holds an opposing blockade
    public bool PathBlocked(Token t, int targetSteps)
    {
        for (int s = t.StepsTaken + 1; s <= targetSteps; s++)
        {
            int cell = _board.GetPathIndex(t.PlayerColor, s);

            if (IsBlockedCell(t.PlayerColor, cell))
                return true;
        }

        return false;
    }

    // Every blockade on the board: shared-track cell index and the color holding it
    public List<(int Index, string Color)> GetBlockades()
    {
        var result = new List<(int Index, string Color)>();

        var groups = _board.AllTokens
            .Select(t => (Token: t, Cell: _board.GetPathIndex(t.PlayerColor, t.StepsTaken)))
            .Where(x => x.Cell >= 0 && !_board.IsSafeIndex(x.Cell))
            .GroupBy(x => (x.Cell, Team: TeamKey(x.Token.PlayerColor)));

        foreach (var group in groups)
        {
            if (group.Count() >= 2)
                result.Add((group.Key.Cell, group.First().Token.PlayerColor));
        }

        return result;
    }

    // Step count the token ends on after this roll
    public int TargetSteps(Token t, int dice)
    {
        if (t.IsInYard) return 0;

        return Math.Min(HomeSteps, t.StepsTaken + dice * Multiplier(t));
    }

    // The tokens a player may move. In team games, a player who has brought all
    // their own tokens home moves their partner's tokens instead.
    public IEnumerable<Token> ControlledTokens(string color)
    {
        string partner = GameConfig.PartnerOf(color);
        bool controlsPartner = _config.IsTeams && AllFinished(color);

        return _board.AllTokens.Where(t =>
            t.PlayerColor == color || (controlsPartner && t.PlayerColor == partner));
    }

    public bool AllFinished(string color)
    {
        return _board.AllTokens.Where(t => t.PlayerColor == color).All(t => t.IsFinished);
    }

    // Opposing tokens sent home by this token's landing. Stars, start cells and
    // home lanes are safe; teammates and yourself are never captured.
    public List<Token> FindCaptures(Token mover)
    {
        var result = new List<Token>();

        if (_board.IsSafe(mover.PlayerColor, mover.StepsTaken))
            return result;

        int cell = _board.GetPathIndex(mover.PlayerColor, mover.StepsTaken);

        foreach (Token other in _board.AllTokens)
        {
            if (_config.SameTeam(mover.PlayerColor, other.PlayerColor)) continue;

            if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                result.Add(other);
        }

        return result;
    }

    // A player wins when all their tokens are home; a team wins when both partners' are.
    public bool HasWon(string color)
    {
        return _board.AllTokens
            .Where(t => _config.SameTeam(color, t.PlayerColor))
            .All(t => t.IsFinished);
    }

    public string WinTitle(string color)
    {
        if (!_config.IsTeams) return $"{color} wins!";

        var pair = new List<string> { color, GameConfig.PartnerOf(color) };
        pair.Sort((a, b) => Array.IndexOf(GameConfig.Order, a).CompareTo(Array.IndexOf(GameConfig.Order, b)));
        return $"{pair[0]} & {pair[1]} win!";
    }

    public static string Ordinal(int n)
    {
        return n switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{n}th"
        };
    }
}
