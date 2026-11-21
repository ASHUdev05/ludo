using Godot;
using System.Collections.Generic;

// Decides which token a bot moves. Each possible move gets a score; the best wins.
public class BotBrain
{
    private readonly BoardManager _board;
    private readonly GameConfig _config;
    private readonly GameRules _rules;
    private readonly RandomNumberGenerator _rng;

    public BotBrain(BoardManager board, GameConfig config, GameRules rules, RandomNumberGenerator rng)
    {
        _board = board;
        _config = config;
        _rules = rules;
        _rng = rng;
    }

    public Token ChooseMove(IReadOnlyList<Token> movable, int dice)
    {
        Token best = movable[0];
        float bestScore = float.MinValue;

        foreach (Token t in movable)
        {
            // Small random term breaks ties so bots aren't perfectly predictable
            float score = Score(t, dice) + _rng.Randf() * 3f;

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    public float Score(Token t, int dice)
    {
        string color = t.PlayerColor;
        int from = t.StepsTaken;
        int to = _rules.TargetSteps(t, dice);

        float score = 0f;

        // A pair that is not split by this move stays protected while it moves
        bool staysPair = t.PairedWith != null && !_rules.PairBreaks(t, dice);

        // Getting a token out of the yard is valuable; otherwise prefer progress
        if (t.IsInYard) score += 40f;
        else score += to * 0.5f;

        // Capturing: more valuable the further the victim had travelled
        if (!_board.IsSafe(color, to))
        {
            int cell = _board.GetPathIndex(color, to);

            foreach (Token other in _board.AllTokens)
            {
                if (_config.SameTeam(color, other.PlayerColor)) continue;

                if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                    score += 100f + other.StepsTaken * 2f;
            }
        }

        // Getting home / into the home lane
        if (to == GameRules.HomeSteps) score += 80f;
        else if (to > 50 && from <= 50) score += 50f;

        // Reaching a safe square
        if (to <= 50 && _board.IsSafe(color, to)) score += 25f;

        // Pairs: forming one is good, leaving one behind is slightly bad
        if (to <= 50 && !_board.IsSafe(color, to) && TeammatesAt(t, to) > 0) score += 20f;
        if (!staysPair && from >= 0 && from <= 50 && !_board.IsSafe(color, from) && TeammatesAt(t, from) > 0) score -= 10f;

        // Danger: run away from threats, avoid walking into them
        if (from >= 0 && IsThreatened(t, from)) score += 30f + from * 0.2f;
        if (!staysPair && IsThreatened(t, to)) score -= 35f;

        return score;
    }

    // How many other tokens of this token's team stand on the cell for the given step count
    private int TeammatesAt(Token self, int steps)
    {
        int cell = _board.GetPathIndex(self.PlayerColor, steps);
        if (cell < 0) return 0;

        int count = 0;

        foreach (Token other in _board.AllTokens)
        {
            if (other == self) continue;
            if (!_config.SameTeam(self.PlayerColor, other.PlayerColor)) continue;

            if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                count++;
        }

        return count;
    }

    // True if an opposing token could land on this token's cell with a single roll (1-6),
    // taking multipliers and blockades into account
    private bool IsThreatened(Token self, int steps)
    {
        string color = self.PlayerColor;

        if (_board.IsSafe(color, steps)) return false;

        // Standing with a teammate forms a blockade, which can't be captured
        if (TeammatesAt(self, steps) > 0) return false;

        int loop = _board.MainPathPositions.Count;
        int cell = _board.GetPathIndex(color, steps);

        foreach (Token other in _board.AllTokens)
        {
            if (_config.SameTeam(color, other.PlayerColor)) continue;

            int otherCell = _board.GetPathIndex(other.PlayerColor, other.StepsTaken);
            if (otherCell < 0) continue;

            int distance = (cell - otherCell + loop) % loop;
            int multiplier = _rules.Multiplier(other);

            bool reachable = distance >= multiplier
                && distance % multiplier == 0
                && distance / multiplier <= 6;

            // It must still be on the shared track when it gets there, and nothing may block its way
            if (reachable
                && other.StepsTaken + distance <= 50
                && !_rules.PathBlocked(other, other.StepsTaken + distance))
                return true;
        }

        return false;
    }
}
