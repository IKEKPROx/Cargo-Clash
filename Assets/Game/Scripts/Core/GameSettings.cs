using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// What each player sets for themselves. Kept on their own computer, and never affects the rules.
    /// Automatic tests can change these for one run, but what they change isn't saved.
    /// </summary>
    public static class GameSettings
    {
        public const float MinLookSpeed = 0.25f;
        public const float MaxLookSpeed = 3f;

        private const string LookSpeedKey = "cc.lookSpeed";
        private const string FirstPersonKey = "cc.firstPerson";
        private const string CrewKey = "cc.crew";

        private static float lookSpeed = -1f;
        private static int firstPerson = -1;
        private static CrewLook? look;

        /// <summary>The body type, hat, glasses and face picked on the crew page, for every match after.</summary>
        public static CrewLook Look
        {
            get
            {
                look ??= CrewLook.Load(PlayerPrefs.GetString(CrewKey, ""));
                return look.Value;
            }
            set
            {
                look = value.Sanitized();
                if (!AutoPilot.Active) PlayerPrefs.SetString(CrewKey, look.Value.Save());
            }
        }

        /// <summary>Scales how far the view turns for a given mouse movement. 1 is the default.</summary>
        public static float LookSpeed
        {
            get
            {
                if (lookSpeed < 0f) lookSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(LookSpeedKey, 1f), MinLookSpeed, MaxLookSpeed);
                return lookSpeed;
            }
            set
            {
                lookSpeed = Mathf.Clamp(value, MinLookSpeed, MaxLookSpeed);
                if (!AutoPilot.Active) PlayerPrefs.SetFloat(LookSpeedKey, lookSpeed);
            }
        }

        public static bool FirstPerson
        {
            get
            {
                if (firstPerson < 0) firstPerson = PlayerPrefs.GetInt(FirstPersonKey, 1);
                return firstPerson == 1;
            }
            set
            {
                firstPerson = value ? 1 : 0;
                if (!AutoPilot.Active) PlayerPrefs.SetInt(FirstPersonKey, firstPerson);
            }
        }
    }
}
