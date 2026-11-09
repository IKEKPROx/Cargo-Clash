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

        public const string CollectorRole = "YOU ARE COLLECTORS";
        public const string InterrupterRole = "YOU ARE INTERRUPTERS";
        public const string CollectorGoal = "Carry cargo to the truck!";
        public const string InterrupterGoal = "Knock carriers down and smash the cargo!";
        public const string CollectorChip = "COLLECTOR";
        public const string InterrupterChip = "INTERRUPTER";
        public const string SidesSwapped = "HALF-TIME: SIDES SWAP";
        public const string TimeUp = "TIME!";
        public const string NoCargoLeft = "NO CARGO LEFT!";
        public const string HelicopterIncoming = "HELICOPTER INCOMING!";

        public static string SecondsLeft(int seconds) => seconds + " SECONDS LEFT!";

        public static string HelicopterLeaves(int rescuedValue) => rescuedValue > 0
            ? "THE HELICOPTER SAVES " + rescuedValue + " IN CARGO!"
            : "THE HELICOPTER LEAVES WITH AN EMPTY BOX!";

        public const string KeysHint = "WASD move    MOUSE look    E pick up / drop    LEFT CLICK hit / smash    SPACE ram    V camera";

        public const string KeyInteract = "E";
        public const string KeyAttack = "CLICK";
        public const string KeyRam = "SPACE";
        public const string PromptPickUp = "PICK UP";
        public const string PromptCarry = "CARRY";
        public const string PromptLarge = "GRAB (NEEDS 2)";
        public const string PromptHelp = "HELP CARRY";
        public const string PromptSmash = "HOLD TO SMASH";
        public const string PromptHit = "HIT";
        public const string PromptDrop = "DROP";
        public const string PromptLetGo = "LET GO";
        public const string PromptWaiting = "Waiting for a second carrier...";
        public const string PromptToTruck = "Carry it to the truck!";
        public const string PromptTogether = "Walk the same way together!";
        public const string RamReady = "RAM";
        public const string RamCooling = "RECHARGING";
        public const string MarkerTruck = "TRUCK";
        public const string You = "YOU";
        public const string Bonk = "BONK!";
        public const string Pow = "POW!";
        public const string Halftime = "HALF ";
        public const string Paused = "PAUSED";
        public const string KeepPlaying = "KEEP PLAYING";
        public const string LeaveMatch = "LEAVE MATCH";
        public const string LookSpeed = "LOOK SPEED";
        public const string ViewHint = "Press V to switch between first person and over the shoulder.";
        public const string PlayAgain = "PLAY AGAIN";
        public const string BackToLobby = "BACK TO LOBBY";
        public const string MainMenu = "MAIN MENU";
        public const string WaitingForHost = "Waiting for the host...";
        public const string Draw = "DRAW!";
        public const string OrangeWins = "ORANGE WINS!";
        public const string BlueWins = "BLUE WINS!";
        public const string Collectors = "COLLECTORS";
        public const string Interrupters = "INTERRUPTERS";

        public static string CargoValue(int value) => "+" + value;
        public static string Metres(string what, int metres) => what + "  " + metres + "m";

        public const string TitleMover = "Mover Supreme";
        public const string TitleWrecker = "Demolition Chief";
        public const string TitleButterfingers = "Butterfingers";
    }
}
