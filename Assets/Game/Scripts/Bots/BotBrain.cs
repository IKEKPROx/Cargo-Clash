using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace CargoClash
{
    /// <summary>
    /// A simple opponent and teammate. It presses the same buttons a person would, through
    /// <see cref="ICharacterInput"/>, and only ever runs on the host.
    /// </summary>
    public sealed class BotBrain : ICharacterInput, IDisposable
    {
        /// <summary>An interrupter with no ram ready goes after a carrier this close, to hit them with the hammer.</summary>
        private const float HammerChaseRange = 7f;

        private static readonly List<BotBrain> Brains = new List<BotBrain>();

        private readonly PlayerAvatar self;
        private readonly GameConfig config;
        private readonly NavMeshPath path = new NavMeshPath();

        private Vector2 move;
        private bool interact;
        private bool ram;
        private bool attack;
        private bool secondary;
        private float nextSlamAt;

        private bool hasGoal;
        private Vector3 goal;
        private float stopDistance;
        private int corner;
        private bool pathValid;
        private float repathAt;
        private float thinkAt;

        private CargoItem target;
        private float holdingAloneSince = -1f;
        private Vector3 stuckCheckPosition;
        private float stuckCheckAt;
        private float ignoreTargetUntil;

        public BotBrain(PlayerAvatar self)
        {
            this.self = self;
            config = GameConfig.Get();
            Brains.Add(this);
        }

        public void Dispose()
        {
            Brains.Remove(this);
        }

        public Vector2 Move => move;
        public bool LooksAround => false;
        public Vector2 Look => Vector2.zero;
        public bool SwitchViewPressed => false;
        public bool InteractPressed => interact;
        public bool RamPressed => ram;
        public bool AttackHeld => attack;
        public bool SecondaryHeld => secondary;
        public bool InspectPressed => false;

        public void Tick()
        {
            interact = false;
            ram = false;

            if (Time.time >= thinkAt)
            {
                thinkAt = Time.time + 0.18f + Random.value * 0.14f;
                Think();
            }
            move = Steer();
        }

        private void Think()
        {
            MatchDirector director = MatchDirector.Instance;
            if (director == null || director.Phase.Value != MatchPhase.Playing || self.IsKnockedDown)
            {
                hasGoal = false;
                attack = false;
                secondary = false;
                return;
            }

            CheckStuck();
            CargoItem carried = self.CarriedCargo;
            if (carried != null)
            {
                attack = false;
                secondary = false;
                ThinkCarrying(carried);
                return;
            }

            holdingAloneSince = -1f;
            if (self.Side == Side.Collector) ThinkCollector();
            else ThinkInterrupter();
        }

        private void ThinkCarrying(CargoItem carried)
        {
            if (self.Side == Side.Interrupter)
            {
                // Interrupter bots smash cargo where it lies rather than dragging it away.
                interact = true;
                return;
            }

            bool coCarry = carried.Definition.requiredCarriers > 1;
            if (coCarry && carried.HasFreeSlot)
            {
                hasGoal = false;
                if (holdingAloneSince < 0f) holdingAloneSince = Time.time;
                if (Time.time - holdingAloneSince > 7f)
                {
                    // Nobody came to help. Go do something useful instead.
                    interact = true;
                    ignoreTargetUntil = Time.time + 8f;
                    target = null;
                }
                return;
            }

            holdingAloneSince = -1f;
            SetGoal(LevelLayout.Instance.ClosestTruckZonePoint(self.transform.position), 0.5f);
        }

        private void ThinkCollector()
        {
            attack = false;
            secondary = false;
            PlayerAvatar smasher = NearestEnemy(9f, enemy => enemy.Swinging.Value);
            if (smasher != null && self.RamReady)
            {
                ChaseAndRam(smasher);
                return;
            }

            PlayerAvatar threat = FindThreatToCarrier();
            if (threat != null && self.RamReady)
            {
                ChaseAndRam(threat);
                return;
            }

            // No ram ready, but someone's busy swinging or carrying within reach: give them a blast.
            PlayerAvatar busy = NearestEnemy(config.blastRange, enemy => !enemy.IsImmune && (enemy.Swinging.Value || enemy.IsCharging || enemy.CarriedCargoId.Value != NetIds.None));
            if (busy != null)
            {
                attack = true;
                SetGoal(busy.transform.position, 1f);
                return;
            }

            target = PickCargoForCollector();
            if (target == null)
            {
                hasGoal = false;
                return;
            }

            SetGoal(target.transform.position, 0.9f + target.Radius * 0.5f);
            float distance = target.EdgeDistance(self.transform.position);
            if (distance <= config.pickupRange - 0.2f) interact = true;
            // Walking toward cargo it can reach, it sucks it in the rest of the way.
            else secondary = target.CanBeSuckedFrom(self.transform.position, self.transform.forward);
        }

        private CargoItem PickCargoForCollector()
        {
            CargoItem best = null;
            float bestScore = float.MinValue;
            foreach (CargoItem cargo in CargoItem.All)
            {
                bool large = cargo.Definition.requiredCarriers > 1;
                bool waitingForHelp = cargo.State.Value == CargoState.Carried && cargo.HasFreeSlot && CarriedByTeammate(cargo);
                if (cargo.State.Value != CargoState.Ground && !waitingForHelp) continue;
                if (large && !waitingForHelp && Time.time < ignoreTargetUntil) continue;

                int claims = ClaimsOn(cargo);
                if (cargo != target && claims >= cargo.Definition.requiredCarriers) continue;

                // Rough points per second: running there, carrying it back, and the risk of a fight.
                float reachTime = cargo.EdgeDistance(self.transform.position) / config.runSpeed;
                Vector3 truckPoint = LevelLayout.Instance.ClosestTruckZonePoint(cargo.transform.position);
                float carrySpeed = config.runSpeed * cargo.Definition.carrySpeedMultiplier;
                float carryTime = Vector3.Distance(cargo.transform.position, truckPoint) / carrySpeed;
                float score = cargo.Definition.value / (reachTime + carryTime + 1.5f);
                if (large && !waitingForHelp) score *= 0.5f;
                if (NearestEnemyTo(cargo.transform.position, 4f) != null) score *= 0.6f;
                if (waitingForHelp) score += 5f;
                if (cargo == target) score *= 1.25f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cargo;
                }
            }
            return best;
        }

        private void ThinkInterrupter()
        {
            secondary = false;
            if (ThinkSlam()) return;

            PlayerAvatar carrier = NearestEnemy(14f, enemy => enemy.CarriedCargoId.Value != NetIds.None && !enemy.IsImmune);
            if (carrier != null && self.RamReady)
            {
                attack = false;
                ChaseAndRam(carrier);
                return;
            }

            PlayerAvatar attacker = NearestEnemy(3.5f, enemy => !enemy.IsImmune);
            if (attacker != null && self.RamReady)
            {
                attack = false;
                ChaseAndRam(attacker);
                return;
            }

            // The ram is recharging, but the hammer works on people too. It just takes two hits.
            PlayerAvatar closeCarrier = NearestEnemy(HammerChaseRange, enemy => !enemy.IsImmune && enemy.CarriedCargoId.Value != NetIds.None);
            if (closeCarrier != null)
            {
                target = null;
                attack = Vector3.Distance(closeCarrier.transform.position, self.transform.position) <= config.hitRange;
                SetGoal(closeCarrier.transform.position, 1f);
                return;
            }
            PlayerAvatar withinReach = NearestEnemy(config.hitRange, enemy => !enemy.IsImmune);
            if (withinReach != null)
            {
                attack = true;
                SetGoal(withinReach.transform.position, 1f);
                return;
            }

            target = PickCargoForInterrupter();
            if (target == null)
            {
                attack = false;
                PlayerAvatar anyCarrier = NearestEnemy(200f, enemy => enemy.CarriedCargoId.Value != NetIds.None);
                if (anyCarrier != null) SetGoal(anyCarrier.transform.position, 1f);
                else hasGoal = false;
                return;
            }

            float distance = target.EdgeDistance(self.transform.position);
            SetGoal(target.transform.position, 0.8f + target.Radius * 0.5f);
            attack = distance <= config.smashRange - 0.2f;
        }

        /// <summary>Now and then, with someone close, it charges up a slam and walks at them. Returns true while it's doing that.</summary>
        private bool ThinkSlam()
        {
            PlayerAvatar enemy = NearestEnemy(5f, other => !other.IsImmune);
            if (self.IsCharging)
            {
                attack = false;
                if (enemy == null) return false;
                SetGoal(enemy.transform.position, 1f);
                bool inReach = Vector3.Distance(enemy.transform.position, self.transform.position) <= config.slamReach + config.slamRadius - 0.4f;
                // Keeps holding until it's ready and they're in reach. Not holding any more is what lets it go.
                secondary = self.Charge01 < 1f || !inReach;
                return true;
            }

            if (enemy == null || Time.time < nextSlamAt) return false;
            nextSlamAt = Time.time + Random.Range(5f, 10f);
            if (Random.value > 0.6f) return false;
            attack = false;
            secondary = true;
            SetGoal(enemy.transform.position, 1f);
            return true;
        }

        private CargoItem PickCargoForInterrupter()
        {
            CargoItem best = null;
            float bestScore = float.MinValue;
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value != CargoState.Ground) continue;
                float distance = cargo.EdgeDistance(self.transform.position);
                float score = (cargo.Definition.value + 10f) / (distance + 6f);
                if (cargo.Durability.Value < 1f) score *= 1.4f;
                if (cargo == target) score += 0.5f;
                if (ClaimsOn(cargo) > 0 && cargo != target) score *= 0.6f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cargo;
                }
            }
            return best;
        }

        private void ChaseAndRam(PlayerAvatar enemy)
        {
            target = null;
            SetGoal(enemy.transform.position, 0.5f);
            Vector3 offset = enemy.transform.position - self.transform.position;
            offset.y = 0f;
            if (offset.magnitude <= config.ramSpeed * config.ramDuration * 0.85f) ram = true;
        }

        private PlayerAvatar NearestEnemy(float range, Func<PlayerAvatar, bool> condition)
        {
            PlayerAvatar best = null;
            float bestDistance = range;
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other.Team == self.Team || other.IsKnockedDown || !condition(other)) continue;
                float distance = Vector3.Distance(other.transform.position, self.transform.position);
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>An enemy closing in on a teammate who carries valuable cargo. Free collectors escort them.</summary>
        private PlayerAvatar FindThreatToCarrier()
        {
            foreach (PlayerAvatar mate in PlayerAvatar.All)
            {
                if (mate == self || mate.Team != self.Team) continue;
                CargoItem carried = mate.CarriedCargo;
                if (carried == null || carried.Definition.size == CargoSize.Small) continue;
                if (Vector3.Distance(mate.transform.position, self.transform.position) > 16f) continue;
                PlayerAvatar enemy = NearestEnemyTo(mate.transform.position, 8f);
                if (enemy != null && !enemy.IsImmune && !enemy.IsKnockedDown) return enemy;
            }
            return null;
        }

        private PlayerAvatar NearestEnemyTo(Vector3 position, float range)
        {
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other.Team != self.Team && Vector3.Distance(other.transform.position, position) < range) return other;
            }
            return null;
        }

        private bool CarriedByTeammate(CargoItem cargo)
        {
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other.Team == self.Team && other != self && other.CarriedCargoId.Value == cargo.NetworkObjectId)
                    return true;
            }
            return false;
        }

        private int ClaimsOn(CargoItem cargo)
        {
            int claims = 0;
            foreach (BotBrain brain in Brains)
            {
                if (brain != this && brain.target == cargo && brain.self.Team == self.Team) claims++;
            }
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other != self && other.CarriedCargoId.Value == cargo.NetworkObjectId) claims++;
            }
            return claims;
        }

        private void CheckStuck()
        {
            if (Time.time < stuckCheckAt) return;
            stuckCheckAt = Time.time + 2f;
            bool stuck = hasGoal && move.sqrMagnitude > 0.01f &&
                (self.transform.position - stuckCheckPosition).sqrMagnitude < 0.25f;
            stuckCheckPosition = self.transform.position;
            if (stuck)
            {
                target = null;
                repathAt = 0f;
            }
        }

        private void SetGoal(Vector3 position, float stop)
        {
            if (!hasGoal || (position - goal).sqrMagnitude > 1f) repathAt = 0f;
            hasGoal = true;
            goal = position;
            stopDistance = stop;
        }

        private Vector2 Steer()
        {
            if (!hasGoal) return Vector2.zero;
            Vector3 position = self.transform.position;
            Vector3 toGoal = goal - position;
            toGoal.y = 0f;
            if (toGoal.magnitude <= stopDistance) return Vector2.zero;

            if (Time.time >= repathAt) Repath(position);

            Vector3 direction = toGoal;
            if (pathValid && corner < path.corners.Length)
            {
                Vector3 next = path.corners[corner];
                Vector3 toCorner = next - position;
                toCorner.y = 0f;
                if (toCorner.magnitude < 0.6f && corner < path.corners.Length - 1)
                {
                    corner++;
                    toCorner = path.corners[corner] - position;
                    toCorner.y = 0f;
                }
                direction = toCorner;
            }

            direction.Normalize();
            return new Vector2(direction.x, direction.z);
        }

        private void Repath(Vector3 position)
        {
            repathAt = Time.time + 0.45f;
            pathValid = false;
            if (!NavMesh.SamplePosition(position, out NavMeshHit from, 3f, NavMesh.AllAreas)) return;
            if (!NavMesh.SamplePosition(goal, out NavMeshHit to, 4f, NavMesh.AllAreas)) return;
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path)) return;
            pathValid = path.corners.Length > 0;
            corner = path.corners.Length > 1 ? 1 : 0;
        }
    }
}
