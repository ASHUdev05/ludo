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

        // Is this token moving as part of a pair, and does it stay one?
        bool movesAsPair = t.PairedWith != null;
        bool staysPair = movesAsPair && !_rules.PairBreaks(t, dice);

        // A single token that lands on a free teammate forms a pair (and captures nothing)
        bool joinsTeammate = !movesAsPair && FreeTeammateAt(t, to);
        bool pairAtTarget = staysPair || joinsTeammate;

        float score = 0f;

        // Getting a token out of the yard is valuable; otherwise prefer progress
        if (t.IsInYard) score += 40f;
        else score += to * 0.5f;

        // Capturing: more valuable the further the victim had travelled.
        // A pair captures pairs only; a single token captures unprotected single tokens.
        if (!joinsTeammate)
        {
            foreach (Token victim in _rules.PredictCaptures(t, to, movesAsPair))
                score += 100f + victim.StepsTaken * 2f;
        }

        // Getting home / into the home lane
        if (to == GameRules.HomeSteps) score += 80f;
        else if (to > 50 && from <= 50) score += 50f;

        // Reaching a safe square
        if (to <= 50 && _board.IsSafe(color, to)) score += 25f;

        // Forming a pair on an ordinary cell is good
        if (joinsTeammate && to <= 50 && !_board.IsSafe(color, to)) score += 20f;

        // Danger: run away from threats, avoid walking into them
        if (from >= 0 && IsThreatened(t, from, movesAsPair)) score += 30f + from * 0.2f;
        if (IsThreatened(t, to, pairAtTarget)) score -= 35f;

        return score;
    }

    // True if another free token of this token's team stands on the cell for the given step count
    private bool FreeTeammateAt(Token self, int steps)
    {
        int cell = _board.GetPathIndex(self.PlayerColor, steps);
        if (cell < 0) return false;

        foreach (Token other in _board.AllTokens)
        {
            if (other == self || other.PairedWith != null) continue;
            if (!_config.SameTeam(self.PlayerColor, other.PlayerColor)) continue;

            if (_board.GetPathIndex(other.PlayerColor, other.StepsTaken) == cell)
                return true;
        }

        return false;
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

    // Could an opposing token capture this one next turn?
    //  - asPair: the token is part of a pair, so only an opposing pair can capture it.
    //  - otherwise only opposing single tokens can, and not if it stands with a teammate.
    private bool IsThreatened(Token self, int steps, bool asPair)
    {
        string color = self.PlayerColor;

        if (_board.IsSafe(color, steps)) return false;

        // Standing with a teammate (without being a pair) can't be captured by single tokens
        if (!asPair && TeammatesAt(self, steps) > 0) return false;

        int loop = _board.MainPathPositions.Count;
        int cell = _board.GetPathIndex(color, steps);

        foreach (Token other in _board.AllTokens)
        {
            if (_config.SameTeam(color, other.PlayerColor)) continue;

            int otherCell = _board.GetPathIndex(other.PlayerColor, other.StepsTaken);
            if (otherCell < 0) continue;

            int distance = (cell - otherCell + loop) % loop;
            if (distance == 0) continue;

            if (asPair)
            {
                // Only an opposing pair can capture a pair
                if (other.PairedWith == null) continue;

                bool reachable = false;
                for (int roll = 1; roll <= 6; roll++)
                {
                    if (_rules.PairDistance(other, roll) == distance)
                    {
                        reachable = true;
                        break;
                    }
                }

                if (reachable && !_rules.PathBlocked(other, other.StepsTaken + distance))
                    return true;
            }
            else
            {
                // Pairs never capture single tokens
                if (other.PairedWith != null) continue;

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
        }

        return false;
    }
}
