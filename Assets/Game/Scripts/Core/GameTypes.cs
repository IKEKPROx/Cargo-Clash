using UnityEngine;

namespace CargoClash
{
    public enum Team : byte { Orange = 0, Blue = 1 }

    /// <summary>A team's role for one half. Teams swap sides at half-time.</summary>
    public enum Side : byte { Collector = 0, Interrupter = 1 }

    public enum MatchPhase : byte { Loading = 0, HalfIntro = 1, Playing = 2, HalfOutro = 3, Results = 4 }

    public enum CargoSize : byte { Small = 0, Medium = 1, Large = 2 }

    public enum CargoState : byte { Ground = 0, Carried = 1, Rescued = 2, Destroyed = 3 }

    public static class GameLayers
    {
        public const int Player = 8;
        public const int Cargo = 9;
        public const int Level = 10;
        /// <summary>Vehicles and other models that get an outline but aren't characters or cargo.</summary>
        public const int Outlined = 11;
        /// <summary>The hands and held cargo. A second camera draws this layer on top of everything.</summary>
        public const int ViewModel = 12;
        /// <summary>Bits of broken cargo. They only collide with the level.</summary>
        public const int Debris = 13;
        public static int LevelMask => 1 << Level;
    }

    public static class NetIds
    {
        public const ulong None = ulong.MaxValue;
    }

    public static class TeamExtensions
    {
        public static Team Other(this Team team) => team == Team.Orange ? Team.Blue : Team.Orange;

        public static Color ToColor(this Team team) =>
            team == Team.Orange ? new Color(1f, 0.56f, 0.13f) : new Color(0.25f, 0.6f, 1f);

        public static string ToLabel(this Team team) => team == Team.Orange ? "ORANGE" : "BLUE";
    }
}
