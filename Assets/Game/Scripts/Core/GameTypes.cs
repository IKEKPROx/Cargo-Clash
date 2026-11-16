using UnityEngine;

namespace CargoClash
{
    public enum Team : byte { Orange = 0, Blue = 1 }

    /// <summary>A team's role for one half. Teams swap sides at half-time.</summary>
    public enum Side : byte { Collector = 0, Interrupter = 1 }

    public enum MatchPhase : byte { Loading = 0, HalfIntro = 1, Playing = 2, HalfOutro = 3, Results = 4 }

    /// <summary>
    /// Which of Astra's three people someone is. It only changes how they look: Collectors wear the
    /// porters' clothes and Interrupters the wreckers', on the same body.
    /// </summary>
    public enum BodyType : byte { Short = 0, Medium = 1, Tall = 2 }

    public enum CargoSize : byte { Small = 0, Medium = 1, Large = 2 }

    /// <summary>
    /// Astra's four kinds of cargo (独立建模/互动货物_v08), each with its own case and something inside to look at:
    /// a carton of cigarettes, a case of cash, a case of gold bars and the crate with the porcelain goose.
    /// A plank isn't cargo: on the river level it's carried like cargo from the huts to the catcher across the water.
    /// </summary>
    public enum CargoKind : byte { Tobacco = 0, Cash = 1, Gold = 2, Art = 3, Plank = 4, Legacy = 255 }

    /// <summary>Lost: the river carried it off the field. Nobody scores it.</summary>
    public enum CargoState : byte { Ground = 0, Carried = 1, Rescued = 2, Destroyed = 3, Lost = 4 }

    /// <summary>What someone got hit with.</summary>
    public enum Weapon : byte { Hammer = 0, Blast = 1, Slam = 2, Ram = 3 }

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
        /// <summary>The character on the crew page. Only its own camera draws this layer.</summary>
        public const int Preview = 14;
        /// <summary>Walls only people bump into, like the deep water on the river level. Cargo floats on through.</summary>
        public const int PlayerBlock = 15;
        /// <summary>Only the rain lands on these, like the hut roofs. Nothing else touches them.</summary>
        public const int RainOnly = 16;
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

        public static string ToLabel(this Team team) => team == Team.Orange ? GameText.TeamOrange : GameText.TeamBlue;
    }
}
