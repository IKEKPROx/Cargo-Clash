namespace CargoClash
{
    /// <summary>Player-facing strings in one place, so changing or translating them is easy.</summary>
    public static class GameText
    {
        public const string HostLeft = "The host left the game.";
        public const string JoinFailed = "Could not join that room. Check the code and try again.";
        public const string NeedCloudProject = "Online rooms need the Unity Cloud project. Use the host's LAN address for now.";
        public const string MatchInProgress = "That match has already started.";
        public const string RoomFull = "That room is full.";

        public const string CollectorGoal = "YOU ARE COLLECTORS\nCarry cargo to the truck!";
        public const string InterrupterGoal = "YOU ARE INTERRUPTERS\nKnock carriers down and smash the cargo!";
        public const string SidesSwapped = "HALF-TIME: SIDES SWAP";
        public const string TimeUp = "TIME!";
        public const string LastThirty = "30 SECONDS LEFT!";

        public const string KeysHint = "WASD move    E pick up / drop    SPACE ram    F hold to smash (Interrupters)";

        public const string PromptPickUp = "E  pick up";
        public const string PromptLarge = "E  grab (needs 2 people)";
        public const string PromptSmash = "F  smash     E  carry";
        public const string PromptWaiting = "Waiting for a second carrier...";
        public const string PromptToTruck = "Carry it to the truck!";

        public const string TitleMover = "Mover Supreme";
        public const string TitleWrecker = "Demolition Chief";
        public const string TitleButterfingers = "Butterfingers";
    }
}
