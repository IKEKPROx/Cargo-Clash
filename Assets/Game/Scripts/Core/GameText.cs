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

        public const string Play = "PLAY";
        public const string Practice = "PRACTICE WITH BOTS";
        public const string CreateRoomButton = "CREATE ROOM";
        public const string Join = "JOIN";
        public const string Quit = "QUIT";
        public const string CodePlaceholder = "ROOM CODE";
        public const string Loading = "Loading...";
        public const string CreatingRoom = "Creating room...";
        public const string Joining = "Joining...";
        public const string PracticeFailed = "Could not start practice mode.";
        public const string CreateFailed = "Could not create a room.";

        public const string ChooseLevel = "CHOOSE A LEVEL";
        public const string Back = "BACK";
        public const string ComingSoon = "COMING SOON";
        public const string LevelHint = "New here? Start with the tutorial.";
        public const string LevelLocked = "That level isn't ready yet. Try the tutorial!";

        public const string TeamOrange = "ORANGE";
        public const string TeamBlue = "BLUE";
        public const string JoinOrange = "JOIN ORANGE";
        public const string JoinBlue = "JOIN BLUE";
        public const string Ready = "READY";
        public const string NotReady = "NOT READY";
        public const string StartGame = "START GAME";
        public const string Leave = "LEAVE";
        public const string LobbyAllReady = "Everyone is ready.";
        public const string LobbyWaitForReady = "Waiting for everyone to get ready, with even teams.";
        public const string LobbyWaitForHost = "Waiting for the host to start.";

        public static string BotsFill(bool on) => on ? "BOTS FILL: ON" : "BOTS FILL: OFF";
        public static string RoomCode(string code) => "ROOM CODE: " + code;
        public static string LanRoom(string address) => "LAN ROOM: " + address;
        public static string LevelLine(int number, string title) => "LEVEL " + number + ": " + title;
        public static string RosterLine(string name, bool isYou, bool ready) => name + (isYou ? " (you)" : "") + (ready ? "  READY" : "");

        public const string CollectorGoal = "YOU ARE COLLECTORS\nCarry cargo to the truck!";
        public const string InterrupterGoal = "YOU ARE INTERRUPTERS\nKnock carriers down and smash the cargo!";
        public const string SidesSwapped = "HALF-TIME: SIDES SWAP";
        public const string TimeUp = "TIME!";
        public const string NoCargoLeft = "NO CARGO LEFT!";
        public const string HelicopterIncoming = "HELICOPTER INCOMING!";

        public static string SecondsLeft(int seconds) => seconds + " SECONDS LEFT!";

        public static string HelicopterLeaves(int rescuedValue) => rescuedValue > 0
            ? "THE HELICOPTER SAVES " + rescuedValue + " IN CARGO!"
            : "THE HELICOPTER LEAVES WITH AN EMPTY BOX!";

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
