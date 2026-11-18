using System.Collections.Generic;
using System.Linq;

public enum SeatType { Off, Human, Bot }

public enum GameMode { TwoPlayers, ThreePlayers, FourPlayers, Teams }

// Plain data describing how a game is set up. Not a Godot node.
public class GameConfig
{
    // Clockwise around the board
    public static readonly string[] Order = { "Red", "Green", "Yellow", "Blue" };

    // Remembered between games (survives scene reloads)
    public static GameConfig Last;

    public GameMode Mode = GameMode.FourPlayers;

    public Dictionary<string, SeatType> Seats = new()
    {
        { "Red", SeatType.Human },
        { "Green", SeatType.Human },
        { "Yellow", SeatType.Human },
        { "Blue", SeatType.Human }
    };

    public bool IsTeams => Mode == GameMode.Teams;

    public int RequiredPlayers => Mode switch
    {
        GameMode.TwoPlayers => 2,
        GameMode.ThreePlayers => 3,
        _ => 4
    };

    public IEnumerable<string> ActiveColors =>
        Order.Where(c => Seats[c] != SeatType.Off);

    // Teams are the opposite seats: Red + Yellow vs Green + Blue
    public static string PartnerOf(string color) => color switch
    {
        "Red" => "Yellow",
        "Yellow" => "Red",
        "Green" => "Blue",
        _ => "Green"
    };

    public bool SameTeam(string a, string b)
    {
        return a == b || (IsTeams && PartnerOf(a) == b);
    }

    public GameConfig Clone()
    {
        var copy = new GameConfig { Mode = Mode };
        foreach (var pair in Seats)
            copy.Seats[pair.Key] = pair.Value;
        return copy;
    }
}