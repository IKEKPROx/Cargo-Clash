using UnityEngine;
using UnityEngine.InputSystem;

namespace CargoClash
{
    /// <summary>What drives a character. The keyboard and the bots both go through this.</summary>
    public interface ICharacterInput
    {
        Vector2 Move { get; }
        bool InteractPressed { get; }
        bool RamPressed { get; }
        bool SmashHeld { get; }

        /// <summary>Called once a frame before the character reads the values above.</summary>
        void Tick();
    }

    public sealed class HumanInput : ICharacterInput
    {
        private readonly InputAction move;
        private readonly InputAction interact;
        private readonly InputAction ram;
        private readonly InputAction smash;

        public HumanInput(InputActionAsset asset)
        {
            InputActionMap map = asset.FindActionMap("Gameplay", true);
            map.Enable();
            move = map.FindAction("Move", true);
            interact = map.FindAction("Interact", true);
            ram = map.FindAction("Ram", true);
            smash = map.FindAction("Smash", true);
        }

        public Vector2 Move => move.ReadValue<Vector2>();
        public bool InteractPressed => interact.WasPressedThisFrame();
        public bool RamPressed => ram.WasPressedThisFrame();
        public bool SmashHeld => smash.IsPressed();

        public void Tick() { }
    }
}
