using System.Collections.Generic;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Player-facing strings in one place, in the player's language (GameSettings.Language). The English is the key:
    /// T looks it up in the Chinese table below, and anything missing from the table shows in English. Labels built into
    /// the scenes keep their English in LocalizedText and change when the language does. In the Editor (the scene
    /// builder) everything is English, so the scenes are always built in English.
    /// </summary>
    public static partial class GameText
    {
        /// <summary>The player's language for this English. A `context` tells apart the same English used two ways.</summary>
        public static string T(string english, string context = null)
        {
            if (string.IsNullOrEmpty(english) || !Application.isPlaying || GameSettings.Language != Language.Chinese) return english;
            return Chinese.TryGetValue(context == null ? english : english + "|" + context, out string chinese) ? chinese : english;
        }

        /// <summary>T with a format string, so each language can put the numbers where it wants them.</summary>
        public static string F(string english, params object[] values) => string.Format(T(english), values);

        public static string HostLeft => T("The host left the game.");
        public static string JoinFailed => T("Could not join that room. Check the code and try again.");
        public static string NeedCloudProject => T("Online rooms need the Unity Cloud project. Use the host's LAN address for now.");
        public static string MatchInProgress => T("That match has already started.");
        public static string RoomFull => T("That room is full.");

        public static string Play => T("PLAY");
        public static string Practice => T("PRACTICE WITH BOTS");
        public static string CreateRoomButton => T("CREATE ROOM");
        public static string Join => T("JOIN");
        public static string Quit => T("QUIT");
        public static string CodePlaceholder => T("ROOM CODE");
        public static string Loading => T("Loading...");
        public static string CreatingRoom => T("Creating room...");
        public static string Joining => T("Joining...");
        public static string PracticeFailed => T("Could not start practice mode.");
        public static string CreateFailed => T("Could not create a room.");

        public static string ChooseLevel => T("CHOOSE A LEVEL");
        public static string Back => T("BACK");
        public static string ComingSoon => T("COMING SOON");
        public static string LevelHint => T("New here? Start with the tutorial.");
        public static string LevelLocked => T("That level isn't ready yet. Try the tutorial!");
        /// <summary>A level's name from GameConfig.levels.</summary>
        public static string LevelTitle(string title) => T(title);

        public static string TeamOrange => T("ORANGE");
        public static string TeamBlue => T("BLUE");
        public static string JoinOrange => T("JOIN ORANGE");
        public static string JoinBlue => T("JOIN BLUE");
        public static string Ready => T("READY");
        public static string NotReady => T("NOT READY");
        public static string StartGame => T("START GAME");
        public static string Leave => T("LEAVE");
        public static string LobbyAllReady => T("Everyone is ready.");
        public static string LobbyWaitForReady => T("Waiting for everyone to get ready, with even teams.");
        public static string LobbyWaitForHost => T("Waiting for the host to start.");

        public static string BotsFill(bool on) => on ? T("BOTS FILL: ON") : T("BOTS FILL: OFF");
        public static string RoomCode(string code) => F("ROOM CODE: {0}", code);
        public static string LanRoom(string address) => F("LAN ROOM: {0}", address);
        public static string LevelLine(int number, string title) => F("LEVEL {0}: {1}", number, LevelTitle(title));
        public static string RosterLine(string name, bool isYou, bool ready, BodyType body) =>
            Name(name) + (isYou ? T(" (you)") : "") + "  " + BodyName(body) + (ready ? "  " + T("READY", "status") : "");

        /// <summary>
        /// A player's name as it's shown. The host names everyone "Player 2" or "Bot 3" in English; those show in the
        /// player's own language.
        /// </summary>
        public static string Name(string name)
        {
            if (string.IsNullOrEmpty(name) || GameSettings.Language == Language.English) return name;
            if (name.StartsWith("Player ")) return F("Player {0}", name.Substring(7));
            if (name.StartsWith("Bot ")) return F("Bot {0}", name.Substring(4));
            return name;
        }

        // The crew page.
        public static string Crew => T("CREW");
        public static string YourCrew => T("YOUR CREW");
        public static string StartPractice => T("START");
        public static string Done => T("DONE");
        public static string RandomLook => T("RANDOM");
        public static string SeeWrecker => T("SEE WRECKER CLOTHES");
        public static string SeePorter => T("SEE PORTER CLOTHES");
        public static string HatHeading => T("HAT");
        public static string GlassesHeading => T("GLASSES");
        public static string FaceHeading => T("FACE");
        public static string StatHealth => T("HEALTH");
        public static string StatSpeed => T("SPEED");
        public static string StatDamage => T("DAMAGE");
        private static readonly string[] FaceSliderNames =
        {
            "JAW", "CHIN", "NOSE LENGTH", "NOSE WIDTH", "EYE SPACING",
            "EYE SIZE", "BROWS", "MOUTH", "SMILE", "SQUINT",
        };
        private static readonly string[] HatNames = { "NO HAT", "CLASSIC", "PATCHED", "WIDE BRIM", "HEAD LAMP", "EARMUFFS", "BEACON" };
        private static readonly string[] GlassesNames = { "NO GLASSES", "SQUARE", "ROUND", "VISOR" };

        public static string FaceSliderName(int slider) => T(FaceSliderNames[Mathf.Clamp(slider, 0, FaceSliderNames.Length - 1)]);
        public static string BodyName(BodyType body) => body == BodyType.Short ? T("SHORT") : body == BodyType.Tall ? T("TALL") : T("MEDIUM");

        public static string BodyBlurb(BodyType body) => body == BodyType.Short
            ? T("Slow, but tough, and hits the hardest.")
            : body == BodyType.Tall ? T("Quick, but fragile, and hits softer.") : T("A bit of everything.");

        public static string HatName(int hat) => T(HatNames[Mathf.Clamp(hat, 0, HatNames.Length - 1)]);
        public static string GlassesName(int glasses) => T(GlassesNames[Mathf.Clamp(glasses, 0, GlassesNames.Length - 1)]);
        public static string Percent(float times) => Mathf.RoundToInt(times * 100f) + "%";

        public static string CollectorRole => T("YOU ARE COLLECTORS");
        public static string InterrupterRole => T("YOU ARE INTERRUPTERS");
        public static string CollectorGoal => T("Carry cargo to the truck!");
        public static string InterrupterGoal => T("Knock carriers down and smash the cargo!");
        public static string CollectorChip => T("COLLECTOR");
        public static string InterrupterChip => T("INTERRUPTER");
        public static string SidesSwapped => T("HALF-TIME: SIDES SWAP");
        public static string TimeUp => T("TIME!");
        public static string NoCargoLeft => T("NO CARGO LEFT!");
        public static string HelicopterIncoming => T("HELICOPTER INCOMING!");

        public static string SecondsLeft(int seconds) => F("{0} SECONDS LEFT!", seconds);

        public static string HelicopterLeaves(int rescuedValue) => rescuedValue > 0
            ? F("THE HELICOPTER SAVES {0} IN CARGO!", rescuedValue)
            : T("THE HELICOPTER LEAVES WITH AN EMPTY BOX!");

        public static string KeysHint => T("WASD move   MOUSE look   E carry   LEFT CLICK attack   RIGHT CLICK suck / slam   SPACE ram   F inspect   V camera");

        public static string KeyInteract => "E";
        public static string KeyAttack => T("CLICK");
        public static string KeySecondary => T("R-CLICK");
        public static string KeyRam => T("SPACE");
        public static string PromptPickUp => T("PICK UP");
        public static string PromptCarry => T("CARRY");
        public static string PromptLarge => T("GRAB (NEEDS 2)");
        public static string PromptHelp => T("HELP CARRY");
        public static string PromptSmash => T("HOLD TO SMASH");
        public static string PromptHit => T("HIT");
        public static string PromptBlast => T("BLAST");
        public static string PromptSuck => T("HOLD TO SUCK");
        public static string PromptSucking => T("SUCKING");
        public static string PromptSlam => T("HOLD TO CHARGE");
        public static string PromptCharging => T("CHARGING");
        public static string PromptSlamReady => T("LET GO TO SLAM");
        public static string PromptDrop => T("DROP");
        public static string PromptLetGo => T("LET GO");
        public static string PromptWaiting => T("Waiting for a second carrier...");
        public static string PromptToTruck => T("Carry it to the truck!");
        public static string PromptTogether => T("Walk the same way together!");
        public static string KeyInspect => "F";
        public static string PromptLookInside => T("LOOK INSIDE");
        public static string PromptOpenCrate => T("OPEN IT");
        // The river level's planks and catcher.
        public static string PromptTakePlank => T("TAKE PLANK");
        public static string PromptInsertPlank => T("PUT IN THE CATCHER");
        public static string PromptToCatcher => T("Plug a gap in the catcher downstream!");
        public static string PromptCatcherFull => T("The catcher is full.");
        public static string RamReady => T("RAM");
        public static string RamCooling => T("RECHARGING");
        public static string MarkerTruck => T("TRUCK");
        public static string MarkerCatcher => T("CATCHER");
        public static string You => T("YOU");
        public static string Bonk => T("BONK!");
        public static string Pow => T("POW!");
        public static string Slam => T("SLAM!");
        public static string HealthLabel => "HP";
        public static string HalfLabel(int half) => GameSettings.Language == Language.Chinese ? (half <= 1 ? "上半场" : "下半场") : "HALF " + half;
        public static string Paused => T("PAUSED");
        public static string KeepPlaying => T("KEEP PLAYING");
        public static string LeaveMatch => T("LEAVE MATCH");
        public static string LookSpeed => T("LOOK SPEED");
        public static string ViewHint => T("Press V to switch between first person and over the shoulder.");
        public static string PlayAgain => T("PLAY AGAIN");
        public static string BackToLobby => T("BACK TO LOBBY");
        public static string MainMenu => T("MAIN MENU");
        public static string WaitingForHost => T("Waiting for the host...");
        public static string Draw => T("DRAW!");
        public static string OrangeWins => T("ORANGE WINS!");
        public static string BlueWins => T("BLUE WINS!");
        public static string Collectors => T("COLLECTORS");
        public static string Interrupters => T("INTERRUPTERS");

        public static string CargoValue(int value) => "+" + value;
        /// <summary>The river took it. Nobody scores it.</summary>
        public static string CargoLost(int value) => F("LOST {0}", value);
        public static string Metres(string what, int metres) => what + "  " + metres + "m";

        // Funny titles on the results screen. The host sends them in English and each computer shows them in its own
        // language (ResultLine), so these stay English here.
        public const string TitleMover = "Mover Supreme";
        public const string TitleWrecker = "Demolition Chief";
        public const string TitleButterfingers = "Butterfingers";
        public const string TitleBrawler = "Knockout King";
        public const string TitlePunchingBag = "Punching Bag";

        /// <summary>One line of the results: a title, who won it and by how much.</summary>
        public static string ResultLine(string title, string player, int value) => F("{0}:  {1}  ({2})", T(title), Name(player), value);

        public static string Damage(int amount) => "-" + amount;

        /// <summary>What goes between two names in the feed when one knocks the other out.</summary>
        public static string KnockOutVerb(Weapon weapon)
        {
            switch (weapon)
            {
                case Weapon.Blast: return T("BLEW AWAY");
                case Weapon.Slam: return T("FLATTENED");
                case Weapon.Ram: return T("BULLDOZED");
                default: return T("BONKED");
            }
        }

        public static string KnockedOutBy(string attacker, int seconds) => F("KNOCKED OUT BY {0}  -  BACK IN {1}", Name(attacker), seconds);

        /// <summary>For knocking people out one after another.</summary>
        public static string Streak(int count)
        {
            switch (count)
            {
                case 1: return T("KNOCKOUT!");
                case 2: return T("DOUBLE KNOCKOUT!");
                case 3: return T("TRIPLE KNOCKOUT!");
                default: return T("UNSTOPPABLE!");
            }
        }

        // The settings page.
        public static string Settings => T("SETTINGS");
        public static string SettingsGame => T("GAME");
        public static string SettingsDisplay => T("DISPLAY");
        public static string SettingsSound => T("SOUND");
        public static string SettingLanguage => T("LANGUAGE");
        public static string SettingCamera => T("CAMERA");
        public static string SettingInvertY => T("INVERT MOUSE Y");
        public static string SettingWindow => T("WINDOW");
        public static string SettingResolution => T("RESOLUTION");
        public static string SettingVSync => T("VSYNC");
        public static string SettingMaxFps => T("MAX FPS");
        public static string SettingGraphics => T("GRAPHICS");
        public static string SettingMaster => T("VOLUME");
        public static string SettingMusic => T("MUSIC");
        public static string SettingEffects => T("EFFECTS");
        public static string Apply => T("APPLY");
        public static string SettingsSaved => T("Settings are saved on this computer.");
        public static string DisplayPending => T("Press APPLY to change the window.");
        public static string On => T("ON");
        public static string Off => T("OFF");
        public static string FirstPersonView => T("FIRST PERSON");
        public static string ShoulderView => T("OVER THE SHOULDER");
        public static string FullScreen => T("FULL SCREEN");
        public static string Windowed => T("WINDOWED");
        public static string FollowsVSync => T("SET BY VSYNC");
        public static string NoLimit => T("NO LIMIT");
        public static string Fps(int fps) => fps + " FPS";
        public static string Resolution(int width, int height) => width + " × " + height;
        public static string GraphicsName(GraphicsLevel level) =>
            level == GraphicsLevel.Low ? T("LOW") : level == GraphicsLevel.Medium ? T("MEDIUM", "graphics") : T("HIGH");

        /// <summary>Each language is named in itself, so anyone can find their own.</summary>
        public static string LanguageName(Language language) => language == Language.Chinese ? "简体中文" : "ENGLISH";
    }
}
