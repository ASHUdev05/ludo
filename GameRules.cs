using System;
using System.Collections.Generic;
using System.Linq;

// The rules of Ludo, kept separate from UI and flow so they are easy to read and test.
//
// PAIRS (blockades)
//  - Two tokens of the same team landing on one shared-track cell form a pair. In team games
//    the two partners' tokens can pair with each other.
//  - A pair moves as one piece: both tokens move together by half the roll. A pair is twice as
//    heavy, so only even rolls can move it (2 moves 1 cell, 4 moves 2, 6 moves 3); an odd roll
//    can't be halved and can't move a pair.
//  - A pair stays a pair, and may pass over stars and start squares. It splits back into two free
//    tokens only when it lands on a star or start square, or enters the home lane.
//  - Single tokens cannot capture a pair, or a token standing with a teammate. Only another pair
//    can capture a pair, by landing on it. A pair never captures single tokens.
//  - Opponents cannot jump over a pair. They may stop on its cell (and move on from there).
//  - Stars and start squares are shared safe cells and never hold pairs.
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

    // =====================================
    // MULTIPLIERS (Supermatch)
    // =====================================

    // A token's speed multiplier. It starts at x1 and goes up by one each time the token reaches
    // another player's start square (x2, x3, x4...). Always x1 in other game types.
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

    // =====================================
    // MOVING
    // =====================================

    public bool CanMove(Token t, int dice)
    {
        if (t.IsInYard) return dice == 6;               // need a 6 to leave the yard
        if (t.IsFinished) return false;

        if (t.PairedWith != null)
        {
            // An odd roll can't be halved, so it can't move a pair
            int distance = PairDistance(t, dice);
            if (distance <= 0) return false;

            // Neither token may overshoot home (Supermatch just finishes at home instead)
            if (!IsSupermatch
                && (t.StepsTaken + distance > HomeSteps || t.PairedWith.StepsTaken + distance > HomeSteps))
                return false;

            // The pair moves together, so neither token may be blocked
            return !PathBlocked(t, TargetSteps(t, dice))
                && !PathBlocked(t.PairedWith, TargetSteps(t.PairedWith, dice));
        }

        // Can't overshoot home (Supermatch just finishes at home instead)
        if (!IsSupermatch && t.StepsTaken + dice > HomeSteps) return false;

        return !PathBlocked(t, TargetSteps(t, dice));
    }

    // Step count the token ends on after this roll (a paired token moves with its partner)
    public int TargetSteps(Token t, int dice)
    {
        if (t.IsInYard) return 0;

        if (t.PairedWith != null)
        {
            int target = t.StepsTaken + PairDistance(t, dice);
            return IsSupermatch ? Math.Min(HomeSteps, target) : target;
        }

        return Math.Min(HomeSteps, t.StepsTaken + dice * Multiplier(t));
    }

    // =====================================
    // PAIRS
    // =====================================

    // Half of the roll. A pair is twice as heavy, so it moves half as far; an odd total can't be
    // halved, so it moves 0 (the pair can't move at all). A pair moves at the pace of its slower token.
    private int PairHalf(Token a, int dice)
    {
        int multiplier = Math.Min(Multiplier(a), Multiplier(a.PairedWith));
        int total = dice * multiplier;

        return total % 2 == 0 ? total / 2 : 0;
    }

    // How many cells both tokens of a pair move for this roll (0 if the roll is odd)
    public int PairDistance(Token a, int dice)
    {
        return PairHalf(a, dice);
    }

    // True if this roll takes a token of the pair onto a star or start square, or into the home
    // lane, where the pair splits. A pair may pass over stars and start squares without splitting.
    public bool PairBreaks(Token a, int dice)
    {
        Token b = a.PairedWith;

        if (PairHalf(a, dice) <= 0) return false;

        return _board.IsSafe(a.PlayerColor, TargetSteps(a, dice))
            || _board.IsSafe(b.PlayerColor, TargetSteps(b, dice));
    }

    public void Unpair(Token t)
    {
        if (t.PairedWith != null)
        {
            t.PairedWith.PairedWith = null;
            t.PairedWith = null;
        }
    }

    // Call after a single token has landed: if a free teammate shares its (non-safe) cell,
    // the two form a pair.
    public bool TryFormPair(Token mover)
    {
        int cell = _board.GetPathIndex(mover.PlayerColor, mover.StepsTaken);

        if (cell < 0 || _board.IsSafeIndex(cell) || mover.PairedWith != null)
            return false;

        foreach (Token other in _board.AllTokens)
        {
            if (other == mover || other.PairedWith != null) continue;
            if (!_config.SameTeam(mover.PlayerColor, other.PlayerColor)) continue;
            if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) != cell) continue;

            mover.PairedWith = other;
            other.PairedWith = mover;
            return true;
        }

        return false;
    }

    // Drops pairs that no longer make sense (tokens not on the same non-safe track cell)
    public void NormalizePairs()
    {
        foreach (Token t in _board.AllTokens)
        {
            Token partner = t.PairedWith;
            if (partner == null) continue;

            int cell = _board.GetPathIndex(t.PlayerColor, t.StepsTaken);
            int partnerCell = _board.GetPathIndex(partner.PlayerColor, partner.StepsTaken);

            bool valid = partner.PairedWith == t
                && cell >= 0
                && cell == partnerCell
                && !_board.IsSafeIndex(cell);

            if (!valid)
            {
                t.PairedWith = null;
                if (partner.PairedWith == t) partner.PairedWith = null;
            }
        }
    }

    // =====================================
    // BLOCKADES AND CAPTURES
    // =====================================

    private string TeamKey(string color)
    {
        if (!_config.IsTeams) return color;
        return color == "Red" || color == "Yellow" ? "Team1" : "Team2";
    }

    // Does the shared-track cell hold a pair (or more) that stops a token of "moverColor"?
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

    // True if the token would have to jump over an opposing pair on its way to targetSteps.
    // Landing exactly on the pair's cell is allowed, so the final cell is not checked.
    public bool PathBlocked(Token t, int targetSteps)
    {
        for (int s = t.StepsTaken + 1; s < targetSteps; s++)
        {
            int cell = _board.GetPathIndex(t.PlayerColor, s);

            if (IsBlockedCell(t.PlayerColor, cell))
                return true;
        }

        return false;
    }

    // Every cell holding a pair, for drawing: shared-track cell index and the color holding it
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

    // A token standing with at least one teammate on a shared-track cell can't be captured
    private bool IsProtected(Token t)
    {
        int cell = _board.GetPathIndex(t.PlayerColor, t.StepsTaken);
        if (cell < 0) return false;

        int count = 0;

        foreach (Token other in _board.AllTokens)
        {
            if (!_config.SameTeam(t.PlayerColor, other.PlayerColor)) continue;

            if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                count++;
        }

        return count >= 2;
    }

    // Opposing tokens sent home by landing on a cell. Stars, start squares and home lanes are
    // always safe. A single token captures only tokens that are not standing with a teammate;
    // a pair (asPair) captures opposing pairs only.
    private List<Token> CapturesAt(string moverColor, int cell, bool asPair)
    {
        var result = new List<Token>();

        if (cell < 0 || _board.IsSafeIndex(cell))
            return result;

        foreach (Token other in _board.AllTokens)
        {
            if (_config.SameTeam(moverColor, other.PlayerColor)) continue;
            if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) != cell) continue;

            if (asPair)
            {
                if (other.PairedWith != null) result.Add(other);
            }
            else if (!IsProtected(other))
            {
                result.Add(other);
            }
        }

        return result;
    }

    // Tokens sent home now that "mover" has landed. Pass asPair = true if it moved as part of a pair.
    public List<Token> FindCaptures(Token mover, bool asPair)
    {
        int cell = _board.GetPathIndex(mover.PlayerColor, mover.StepsTaken);
        return CapturesAt(mover.PlayerColor, cell, asPair);
    }

    // Tokens that would be sent home if "t" ended its move on toSteps (used by the bots)
    public List<Token> PredictCaptures(Token t, int toSteps, bool asPair)
    {
        int cell = _board.GetPathIndex(t.PlayerColor, toSteps);
        return CapturesAt(t.PlayerColor, cell, asPair);
    }

    // =====================================
    // WHO MOVES WHAT, WINNING
    // =====================================

    // The tokens a player may move. In team games, a player who has brought all their own tokens
    // home moves their partner's tokens instead, and either partner may move a pair that
    // contains both players' tokens.
    public IEnumerable<Token> ControlledTokens(string color)
    {
        string partner = GameConfig.PartnerOf(color);
        bool controlsPartner = _config.IsTeams && AllFinished(color);

        var own = _board.AllTokens
            .Where(t => t.PlayerColor == color || (controlsPartner && t.PlayerColor == partner))
            .ToList();

        var pairedPartners = own
            .Where(t => t.PairedWith != null && !own.Contains(t.PairedWith))
            .Select(t => t.PairedWith)
            .ToList();

        return own.Concat(pairedPartners).Distinct();
    }

    public bool AllFinished(string color)
    {
        return _board.AllTokens.Where(t => t.PlayerColor == color).All(t => t.IsFinished);
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
