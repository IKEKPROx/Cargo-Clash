using System;
using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// What a player picked before the match: one of Astra's three body types, a hat, glasses and a face.
    /// The body type also sets their health, speed and how hard they hit (GameConfig.Stats); the rest is
    /// only looks. It goes to the host through the lobby, the host gives it to their character, and each
    /// player's own choice is saved on their computer (GameSettings.Look).
    /// </summary>
    [Serializable]
    public struct CrewLook : INetworkSerializable, IEquatable<CrewLook>
    {
        /// <summary>The hats and glasses, in Tools/blender/make_crew.py's order. Choice 0 is none.</summary>
        public static readonly string[] Hats = { "classic", "patched", "wide", "lamp", "earmuffs", "beacon" };
        public static readonly string[] GlassesStyles = { "square", "round", "visor" };

        /// <summary>Astra's ten face sliders. Each goes from -1 to 1, and 0 is the face she made.</summary>
        public static readonly string[] FaceSliders =
        {
            "JawWidth", "ChinLength", "NoseLength", "NoseWidth", "EyeSpacing",
            "EyeSize", "BrowHeight", "MouthWidth", "Smile", "EyeSquint",
        };

        // Each slider is six bits, from -31 to 31, so all ten fit in one number.
        private const int Steps = 31;
        private const int Bits = 6;
        private const ulong Mask = (1UL << Bits) - 1UL;

        public BodyType Body;
        /// <summary>0 for no hat, otherwise one more than its place in Hats.</summary>
        public byte Hat;
        /// <summary>0 for no glasses, otherwise one more than its place in GlassesStyles.</summary>
        public byte Glasses;
        public ulong Face;

        public static CrewLook Default => new CrewLook { Body = BodyType.Medium, Hat = 1 };

        public float FaceValue(int slider)
        {
            int raw = (int)((Face >> (slider * Bits)) & Mask);
            if (raw > Steps) raw -= 1 << Bits;
            return raw / (float)Steps;
        }

        public void SetFace(int slider, float value)
        {
            int raw = Mathf.Clamp(Mathf.RoundToInt(value * Steps), -Steps, Steps);
            ulong bits = (ulong)raw & Mask;
            Face = (Face & ~(Mask << (slider * Bits))) | (bits << (slider * Bits));
        }

        /// <summary>Someone made up: a hat most of the time, glasses some of the time, and a face pulled about a bit.</summary>
        public static CrewLook Random(BodyType body)
        {
            CrewLook look = new CrewLook
            {
                Body = body,
                Hat = (byte)(UnityEngine.Random.value < 0.9f ? UnityEngine.Random.Range(1, Hats.Length + 1) : 0),
                Glasses = (byte)(UnityEngine.Random.value < 0.45f ? UnityEngine.Random.Range(1, GlassesStyles.Length + 1) : 0),
            };
            for (int i = 0; i < FaceSliders.Length; i++) look.SetFace(i, UnityEngine.Random.Range(-0.7f, 0.7f));
            return look;
        }

        public CrewLook Sanitized()
        {
            CrewLook look = this;
            if (!Enum.IsDefined(typeof(BodyType), look.Body)) look.Body = BodyType.Medium;
            if (look.Hat > Hats.Length) look.Hat = 0;
            if (look.Glasses > GlassesStyles.Length) look.Glasses = 0;
            return look;
        }

        /// <summary>For saving: "body,hat,glasses,face".</summary>
        public string Save() => $"{(int)Body},{Hat},{Glasses},{Face}";

        public static CrewLook Load(string saved)
        {
            string[] parts = string.IsNullOrEmpty(saved) ? Array.Empty<string>() : saved.Split(',');
            if (parts.Length != 4 || !int.TryParse(parts[0], out int body) || !byte.TryParse(parts[1], out byte hat) ||
                !byte.TryParse(parts[2], out byte glasses) || !ulong.TryParse(parts[3], out ulong face))
                return Default;
            return new CrewLook { Body = (BodyType)body, Hat = hat, Glasses = glasses, Face = face }.Sanitized();
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Body);
            serializer.SerializeValue(ref Hat);
            serializer.SerializeValue(ref Glasses);
            serializer.SerializeValue(ref Face);
        }

        public bool Equals(CrewLook other) => Body == other.Body && Hat == other.Hat && Glasses == other.Glasses && Face == other.Face;

        public override bool Equals(object other) => other is CrewLook look && Equals(look);

        public override int GetHashCode() => HashCode.Combine(Body, Hat, Glasses, Face);
    }
}
