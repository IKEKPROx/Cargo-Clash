using UnityEngine;
using UnityEngine.InputSystem;

namespace CargoClash
{
    /// <summary>What drives a character. The keyboard and mouse and the bots both go through this.</summary>
    public interface ICharacterInput
    {
        /// <summary>
        /// Where to go. For someone who looks around this is relative to where they're looking.
        /// For a bot it's in world axes, and the bot faces the way it walks.
        /// </summary>
        Vector2 Move { get; }

        bool LooksAround { get; }

        /// <summary>Degrees to turn this frame: x is to the right, y is up.</summary>
        Vector2 Look { get; }

        bool InteractPressed { get; }
        bool RamPressed { get; }
        bool SmashHeld { get; }
        bool SwitchViewPressed { get; }

        /// <summary>Called once a frame before the character reads the values above.</summary>
        void Tick();
    }

    public sealed class HumanInput : ICharacterInput
    {
        private readonly GameConfig config;
        private readonly InputAction move;
        private readonly InputAction look;
        private readonly InputAction turn;
        private readonly InputAction interact;
        private readonly InputAction ram;
        private readonly InputAction smash;
        private readonly InputAction switchView;
        private Vector2 lookThisFrame;

        public HumanInput(GameConfig config)
        {
            this.config = config;
            InputActionMap map = config.inputActions.FindActionMap("Gameplay", true);
            map.Enable();
            move = map.FindAction("Move", true);
            look = map.FindAction("Look", true);
            turn = map.FindAction("Turn", true);
            interact = map.FindAction("Interact", true);
            ram = map.FindAction("Ram", true);
            smash = map.FindAction("Smash", true);
            switchView = map.FindAction("SwitchView", true);
        }

        /// <summary>The mouse belongs to the menu while one is open, and to nothing while the window is in the background.</summary>
        private static bool Blocked => (MatchHud.Instance != null && MatchHud.Instance.MenuOpen) ||
            (!Application.isFocused && !AutoPilot.DrivesInput);

        public Vector2 Move => Blocked ? Vector2.zero : move.ReadValue<Vector2>();
        public bool LooksAround => true;
        public Vector2 Look => lookThisFrame;
        public bool InteractPressed => !Blocked && interact.WasPressedThisFrame();
        public bool RamPressed => !Blocked && ram.WasPressedThisFrame();
        public bool SmashHeld => !Blocked && smash.IsPressed();
        public bool SwitchViewPressed => !Blocked && switchView.WasPressedThisFrame();

        public void Tick()
        {
            if (Blocked)
            {
                lookThisFrame = Vector2.zero;
                return;
            }
            Vector2 mouse = look.ReadValue<Vector2>() * (config.lookDegreesPerPixel * GameSettings.LookSpeed);
            // Arrow keys and the right stick turn at a steady rate, for anyone without a mouse.
            Vector2 keys = turn.ReadValue<Vector2>() * (config.keyTurnDegreesPerSecond * GameSettings.LookSpeed * Time.deltaTime);
            lookThisFrame = mouse + keys;
        }
    }
}
