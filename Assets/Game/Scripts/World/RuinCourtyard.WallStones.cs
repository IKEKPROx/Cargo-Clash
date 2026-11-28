using System;
using System.Collections.Generic;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The breakable walls are stacks of separate stones (make_ruin_maya.py wall_stones). Each stone belongs to a
    /// stage: 1 goes with a collector's first blast, 2 with the second, 3 when the wall comes down. A stone that
    /// goes flies out to its own resting place on the side the wall was pushed to and lies there as a solid
    /// stone, low enough to step onto; a repair flies it back. Where each stone comes to rest depends only on
    /// the stone and the replicated push side, so every machine has the same rubble in the same place. The
    /// flight itself is display only.
    /// </summary>
    public sealed partial class RuinCourtyard
    {
        private sealed class Stone
        {
            public Transform Node;
            public BoxCollider Solid;
            public int Stage;
            public Vector3 Home;
            public Quaternion HomeRotation;
            public Vector3 Size;                       // world size as it stands in the wall
            // Fixed for this stone: how far out and to the side it lands, how it lies, how it flies.
            public float Distance, Lateral, Yaw, Tilt, FlightSeconds, Peak, Turns;
            public Vector3 SpinAxis;
            public StoneMode Now;
            public float Age;
            public Vector3 From, To;
            public Quaternion FromRotation, ToRotation;
        }

        private enum StoneMode { Home, Flying, Lying, Returning }

        // Lying stones block like a low step: a player steps up onto them, a bot's path crosses them.
        private const float LyingSolidHeight = .24f;
        private const float StoneReturnSeconds = .55f;

        private void BuildWallStones(Wall wall, Transform mayaModel, string id)
        {
            Transform root = TryFind(mayaModel, id + " / stones");
            if (root == null) return;
            var stones = new List<Stone>();
            int seed = id.GetHashCode() & 0x7fffffff;
            foreach (Transform node in root)
            {
                int cut = node.name.LastIndexOf("_s", StringComparison.Ordinal);
                if (cut < 0 || !int.TryParse(node.name.Substring(cut + 2), out int stage)) continue;
                Renderer renderer = node.GetComponent<Renderer>();
                var random = new System.Random(seed + stones.Count * 7919);
                float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);
                var stone = new Stone
                {
                    Node = node, Stage = Mathf.Clamp(stage, 1, 3), Home = node.position, HomeRotation = node.rotation,
                    Size = renderer != null ? renderer.bounds.size : Vector3.one * .4f,
                    Distance = Range(1.3f, 3.6f), Lateral = Range(-1f, 1f), Yaw = Range(-40f, 40f), Tilt = Range(-6f, 6f),
                    FlightSeconds = Range(.55f, .9f), Peak = Range(.5f, 1.4f), Turns = Range(1f, 2.5f),
                    SpinAxis = new Vector3(Range(-1f, 1f), Range(-.3f, .3f), Range(-1f, 1f)).normalized,
                };
                var solid = new GameObject(node.name + " solid");
                solid.transform.SetParent(field, false);
                solid.layer = GameLayers.Level;
                stone.Solid = solid.AddComponent<BoxCollider>();
                stone.Solid.enabled = false;
                stones.Add(stone);
            }
            if (stones.Count == 0) throw new InvalidOperationException("Ruin wall has no stones: " + id);
            wall.Stones = stones.ToArray();
            wall.Ground = wall.Bounds.min.y;
            wall.ShownStage = -1;
        }

        /// <summary>How many stages of stones are out: none, 1 or 2 blasts, all down.</summary>
        private static int StagesOut(int state) => state == 2 ? 3 : BlastsIn(state);

        /// <summary>Stones still in place, for the runtime checks.</summary>
        internal int StonesStanding(int index)
        {
            Stone[] stones = walls[index].Stones;
            if (stones == null) return -1;
            int count = 0;
            foreach (Stone stone in stones) if (stone.Now == StoneMode.Home || stone.Now == StoneMode.Returning) count++;
            return count;
        }

        internal int StoneCount(int index) => walls[index].Stones?.Length ?? 0;

        /// <summary>Solid fallen stones of this wall, and the world bounds of each, for the runtime checks.</summary>
        internal List<Bounds> LyingStones(int index)
        {
            var lying = new List<Bounds>();
            foreach (Stone stone in walls[index].Stones ?? Array.Empty<Stone>())
                if (stone.Now == StoneMode.Lying && stone.Solid.enabled) lying.Add(stone.Solid.bounds);
            return lying;
        }

        private void ShowStones()
        {
            float deltaTime = Time.deltaTime;
            for (int i = 0; i < walls.Length; i++)
            {
                Wall wall = walls[i];
                if (wall.Stones == null) continue;
                int target = StagesOut(WallState(i));
                if (target != wall.ShownStage)
                {
                    bool animate = wall.ShownStage >= 0 && Playing;
                    MoveStones(i, wall, wall.ShownStage < 0 ? 0 : wall.ShownStage, target, animate);
                    wall.ShownStage = target;
                }
                foreach (Stone stone in wall.Stones) StepStone(stone, deltaTime);
            }
        }

        /// <summary>Where this stone lies once it is knocked out, on the side the wall was pushed to.</summary>
        private void RestPose(int index, Wall wall, Stone stone, out Vector3 position, out Quaternion rotation)
        {
            Vector3 across = wall.Bounds.size.x < wall.Bounds.size.z ? Vector3.right : Vector3.forward;
            if ((Bits & (1UL << (PushSideBit + index))) == 0) across = -across;
            Vector3 along = Vector3.Cross(Vector3.up, across);
            float height = Mathf.Clamp01((stone.Home.y - wall.Ground) / Mathf.Max(.5f, wall.Bounds.size.y));
            Vector3 spot = stone.Home + across * (wall.Bounds.extents.x < wall.Bounds.extents.z ? wall.Bounds.extents.x : wall.Bounds.extents.z)
                           + across * (stone.Distance * (.7f + .6f * height)) + along * stone.Lateral;
            position = new Vector3(spot.x, wall.Ground + stone.Size.y * .5f - .03f, spot.z);
            rotation = Quaternion.Euler(0f, stone.Yaw, stone.Tilt) * stone.HomeRotation;
        }

        private void LayDown(Stone stone, Vector3 position, Quaternion rotation)
        {
            stone.Node.SetPositionAndRotation(position, rotation);
            stone.Node.localScale = Vector3.one;
            stone.Node.gameObject.SetActive(true);
            // A low box under it, turned with it, the stone's own length and width.
            Transform solid = stone.Solid.transform;
            float yaw = (rotation * Quaternion.Inverse(stone.HomeRotation)).eulerAngles.y;
            solid.SetPositionAndRotation(new Vector3(position.x, position.y - stone.Size.y * .5f + .03f + LyingSolidHeight * .5f,
                                                     position.z), Quaternion.Euler(0f, yaw, 0f));
            stone.Solid.size = new Vector3(stone.Size.x * .9f, LyingSolidHeight, stone.Size.z * .9f);
            stone.Solid.enabled = true;
            stone.Now = StoneMode.Lying;
        }

        private void MoveStones(int index, Wall wall, int from, int to, bool animate)
        {
            foreach (Stone stone in wall.Stones)
            {
                bool goes = stone.Stage > from && stone.Stage <= to || !animate && stone.Stage <= to;
                bool returns = stone.Stage > to && stone.Now != StoneMode.Home;
                if (goes && (!animate || stone.Now == StoneMode.Home || stone.Now == StoneMode.Returning))
                {
                    RestPose(index, wall, stone, out Vector3 rest, out Quaternion restRotation);
                    if (!animate)
                    {
                        LayDown(stone, rest, restRotation);
                        continue;
                    }
                    stone.From = stone.Node.position;
                    stone.FromRotation = stone.Node.rotation;
                    stone.To = rest;
                    stone.ToRotation = restRotation;
                    stone.Age = 0f;
                    stone.Solid.enabled = false;
                    stone.Now = StoneMode.Flying;
                }
                else if (returns)
                {
                    stone.Solid.enabled = false;
                    if (!animate)
                    {
                        stone.Node.SetPositionAndRotation(stone.Home, stone.HomeRotation);
                        stone.Now = StoneMode.Home;
                        continue;
                    }
                    stone.From = stone.Node.position;
                    stone.FromRotation = stone.Node.rotation;
                    stone.To = stone.Home;
                    stone.ToRotation = stone.HomeRotation;
                    // Bottom courses first, so the wall builds up again.
                    stone.Age = -(stone.Home.y - wall.Ground) * .25f;
                    stone.Now = StoneMode.Returning;
                }
            }
        }

        private void StepStone(Stone stone, float deltaTime)
        {
            if (stone.Now != StoneMode.Flying && stone.Now != StoneMode.Returning) return;
            stone.Age += deltaTime;
            bool flying = stone.Now == StoneMode.Flying;
            float seconds = flying ? stone.FlightSeconds : StoneReturnSeconds;
            float t = Mathf.Clamp01(stone.Age / seconds);
            float eased = flying ? t : 1f - (1f - t) * (1f - t);
            Vector3 position = Vector3.Lerp(stone.From, stone.To, eased);
            // An arc, and on the way out a little bounce where it lands.
            float arc = flying ? 4f * t * (1f - t) * stone.Peak : Mathf.Sin(t * Mathf.PI) * .5f;
            if (flying && t > .8f) arc += Mathf.Sin((t - .8f) / .2f * Mathf.PI) * .12f;
            position.y += arc;
            Quaternion rotation = Quaternion.Slerp(stone.FromRotation, stone.ToRotation, eased);
            if (flying) rotation = Quaternion.AngleAxis(360f * stone.Turns * (1f - eased), stone.SpinAxis) * rotation;
            stone.Node.SetPositionAndRotation(position, rotation);
            if (t < 1f) return;
            if (flying) LayDown(stone, stone.To, stone.ToRotation);
            else
            {
                stone.Node.SetPositionAndRotation(stone.Home, stone.HomeRotation);
                stone.Now = StoneMode.Home;
            }
        }
    }
}
