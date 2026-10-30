// What the companion does beyond following: fighting beside the player, fishing with them,
// scouting, and noticing the people around them. The game's own AI does the moment-to-moment
// work (aiming, cover, fishing animations); Claude only picks the stance and the lines.

using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;

namespace KitCompanion
{
    public partial class Kit
    {
        const float CombatRangeMeters = 70f;
        const int PeopleCommentSeconds = 75;
        const int PeopleForgetMinutes = 10;

        // Combat: the stance holds for the whole fight; the target is re-picked as people drop.
        bool combatActive;
        string stance = "guard";
        Ped combatTarget;
        string requestedTargetId = "";
        bool stanceApplied;

        bool fishing;
        DateTime scoutUntil = DateTime.MinValue;
        bool Scouting => DateTime.Now < scoutUntil;

        string pendingEvent;
        DateTime lastPeopleEvent = DateTime.MinValue;
        readonly Dictionary<int, DateTime> seenPeople = new Dictionary<int, DateTime>();

        static readonly uint Revolver = (uint)eWeapon.RevolverCattleman;
        static readonly uint Repeater = (uint)eWeapon.RepeaterCarbine;
        static readonly uint Rod = (uint)eWeapon.FishingRod;
        static readonly uint FishingScenario = (uint)Game.Joaat("WORLD_HUMAN_STAND_FISHING");
        static readonly uint FishingMaleA = (uint)Game.Joaat("WORLD_HUMAN_STAND_FISHING_MALE_A");
        Vector3 fishingSpot;
        bool castLine;
        DateTime fishingSince;
        DateTime lastWhistle = DateTime.MinValue;

        void ArmKit()
        {
            // A so-so shot on purpose: he helps, he doesn't win fights for you.
            kit.Weapons.Give(Revolver, 300);
            kit.Weapons.Give(Repeater, 300);
            kit.Weapons.Select(Revolver, false);
            kit.Accuracy = 35;
        }

        // ---------- every half second ----------

        void UpdateActivities(Ped player)
        {
            UpdateCombat(player);
            if (!combatActive) UpdateFishing(player);
            if (!combatActive && !fishing) UpdateCampfire(player);
            UpdateDrinking(player);
            if (!combatActive && !atFire) UpdatePeople(player);
        }

        // ---------- gunfights ----------

        List<Ped> Hostiles(Ped player)
        {
            return World.GetAllPeds()
                .Where(p => p != null && p.Exists() && !p.IsDead && p.IsHuman
                    && p.Handle != player.Handle && p.Handle != kit.Handle
                    && !p.IsFleeing && !p.IsCowering
                    && (p.IsInCombatAgainst(player) || p.IsInCombatAgainst(kit))
                    && p.Position.DistanceTo(player.Position) < CombatRangeMeters)
                .OrderBy(p => p.Position.DistanceTo(kit.Position))
                .ToList();
        }

        void UpdateCombat(Ped player)
        {
            if (!player.IsInCombat)
            {
                if (combatActive)
                {
                    combatActive = false;
                    combatTarget = null;
                    kit.Weapons.Select(Revolver, false);
                    ApplyAction("follow");
                }
                return;
            }
            if (!combatActive)
            {
                // React at once with the default stance; Claude's call arrives a second later.
                combatActive = true;
                stance = "guard";
                stanceApplied = false;
                fishing = false;
                scoutUntil = DateTime.MinValue;
                kit.Weapons.Select(Revolver, true);
            }

            var hostiles = Hostiles(player);
            if (hostiles.Count == 0) return;

            switch (stance)
            {
                case "guard":
                case "attack":
                    bool targetGood = combatTarget != null && combatTarget.Exists() && !combatTarget.IsDead
                        && !combatTarget.IsFleeing && !combatTarget.IsCowering;
                    if (stance == "attack" && requestedTargetId.Length > 0 && !targetGood)
                        combatTarget = hostiles.FirstOrDefault(h => "p" + h.Handle == requestedTargetId);
                    if (!targetGood || combatTarget == null)
                    {
                        combatTarget = hostiles[0];
                        TASK.TASK_COMBAT_PED(kit.Handle, combatTarget.Handle, 0, 16);
                    }
                    break;
                case "stay_out":
                    if (!stanceApplied)
                    {
                        kit.Task.ClearTasks(false);
                        kit.Task.SeekCoverFrom(hostiles[0], -1);
                        stanceApplied = true;
                    }
                    break;
                case "flee":
                    if (!stanceApplied)
                    {
                        kit.Task.ClearTasks(false);
                        TASK.TASK_SMART_FLEE_PED(kit.Handle, hostiles[0].Handle, 60f, 15000, 0, 3f, 0);
                        stanceApplied = true;
                    }
                    break;
            }
        }

        // Returns true when it handled the action, so ApplyAction skips its own follow logic.
        bool ApplyActivityAction(string action, string target)
        {
            Ped player = Game.Player.Character;
            switch (action)
            {
                case "guard":
                case "attack":
                case "stay_out":
                case "flee":
                    // Outside a fight these mean "stay close", so let ApplyAction follow.
                    if (!combatActive) return false;
                    if (action != stance || action == "attack")
                    {
                        stance = action;
                        stanceApplied = false;
                        combatTarget = null;
                        requestedTargetId = action == "attack" ? target : "";
                        currentAction = action;
                    }
                    return true;
                case "follow":
                case "idle":
                    // Mid-fight or mid-fishing, a plain follow from Claude doesn't pull him out of it.
                    return combatActive || fishing;
                case "mount_up":
                    if (!kit.IsOnMount && roach != null && roach.Exists())
                        kit.Task.MountAnimal(roach, -1, (eVehicleSeat)(-1), 2f, (eEnterExitVehicleFlags)0);
                    return true;
                case "dismount":
                    if (kit.IsOnMount) kit.Task.DismountAnimal((eEnterExitVehicleFlags)0);
                    return true;
                case "scout_ahead":
                    if (combatActive) return true;
                    Vector3 ahead = player.Position + player.ForwardVector * 45f;
                    kit.Task.ClearTasks(false);
                    kit.Task.GoTo(ahead, player.IsOnMount ? 3f : 2f, 20000);
                    scoutUntil = DateTime.Now.AddSeconds(20);
                    currentAction = "scout_ahead";
                    return true;
                case "go_to":
                    var who = World.GetAllPeds().FirstOrDefault(p => p != null && p.Exists() && "p" + p.Handle == target);
                    if (who == null || combatActive) return combatActive;
                    kit.Task.ClearTasks(false);
                    kit.Task.GoTo(who.Position, 1f, 15000);
                    scoutUntil = DateTime.Now.AddSeconds(15);
                    currentAction = "go_to";
                    return true;
            }
            return false;
        }

        // ---------- fishing ----------

        // ---------- campfire ----------
        // Sit down within a few meters of a fire and he sits across it and tells a story,
        // then another every few minutes while you stay.
        const float FireRangeMeters = 6f;
        const int StoryEverySeconds = 300;
        static readonly uint SitGround = (uint)Game.Joaat("WORLD_HUMAN_SIT_GROUND");
        bool atFire, seated;
        Vector3 firePos, fireSeat;
        DateTime fireSince, lastStory, playerStoodAt;

        void UpdateCampfire(Ped player)
        {
            bool sitting = PlayerSitting(player);
            if (!atFire)
            {
                if (sitting != lastSitting) LogFireCheck(player, sitting);
                lastSitting = sitting;
                if (!sitting) return;
                Vector3 fire;
                bool found;
                unsafe { found = FIRE.GET_CLOSEST_FIRE_POS(&fire, player.Position); }
                if (!found || fire.DistanceTo(player.Position) > FireRangeMeters)
                {
                    // Camp fires may not register as the closest fire; any fire in range will do.
                    if (FIRE.GET_NUMBER_OF_FIRES_IN_RANGE(player.Position, FireRangeMeters) == 0) return;
                    if (!found || fire.DistanceTo(player.Position) > 30f) fire = player.Position + player.ForwardVector * 1.5f;
                }

                atFire = true;
                seated = false;
                firePos = fire;
                Vector3 away = fire - player.Position;
                away.Z = 0;
                away.Normalize();
                fireSeat = fire + away * 2.2f;  // across the fire from the player
                fireSince = DateTime.Now;
                lastStory = DateTime.Now;
                playerStoodAt = DateTime.MinValue;
                kit.Task.ClearTasks(false);
                if (kit.IsOnMount) kit.Task.DismountAnimal((eEnterExitVehicleFlags)0);
                else kit.Task.GoTo(fireSeat, 1f, 20000);
                currentAction = "idle";
                AddEvent("the player sat down by a campfire, and the companion sat down across the fire from them");
                pendingEvent = "campfire";
                Log("campfire");
                return;
            }

            if (!sitting)
            {
                // A few seconds' grace: shifting position isn't leaving.
                if (playerStoodAt == DateTime.MinValue) playerStoodAt = DateTime.Now;
                else if ((DateTime.Now - playerStoodAt).TotalSeconds > 4)
                {
                    atFire = false;
                    kit.Task.ClearTasks(false);
                    AddEvent("the player got up from the fire");
                    ApplyAction("follow");
                }
                return;
            }
            playerStoodAt = DateTime.MinValue;

            if (!seated)
            {
                if (!kit.IsOnMount && kit.Position.DistanceTo(fireSeat) > 1.5f && (DateTime.Now - fireSince).TotalSeconds < 15)
                {
                    if (!kit.IsWalking && !kit.IsRunning) kit.Task.GoTo(fireSeat, 1f, 20000);
                    return;
                }
                Vector3 toFire = firePos - kit.Position;
                float heading = (float)(Math.Atan2(-toFire.X, toFire.Y) * 180.0 / Math.PI);
                kit.Task.ClearTasks(true);
                TASK.TASK_START_SCENARIO_IN_PLACE_HASH(kit.Handle, SitGround, -1, true, 0, heading, false);
                seated = true;
            }
            else if ((DateTime.Now - lastStory).TotalSeconds > StoryEverySeconds && pendingEvent == null)
            {
                lastStory = DateTime.Now;
                pendingEvent = "campfire";
            }
        }

        // He drinks when the player drinks: same drunkness, same wobble, sobers up with them.
        float playerDrunk, kitDrunk;
        DateTime lastDrunkCheck;

        void UpdateDrinking(Ped player)
        {
            if ((DateTime.Now - lastDrunkCheck).TotalSeconds < 2) return;
            lastDrunkCheck = DateTime.Now;
            playerDrunk = PED._GET_PED_DRUNKNESS(player.Handle);
            if (playerDrunk > 0.1f && Math.Abs(playerDrunk - kitDrunk) > 0.05f)
            {
                if (kitDrunk <= 0.1f) AddEvent("the player started drinking, and the companion is matching them drink for drink");
                PED._SET_PED_DRUNKNESS(kit.Handle, true, playerDrunk);
                kitDrunk = playerDrunk;
            }
            else if (playerDrunk <= 0.1f && kitDrunk > 0.1f)
            {
                PED._SET_PED_DRUNKNESS(kit.Handle, false, 0f);
                kitDrunk = 0f;
                AddEvent("they've both sobered up");
            }
        }

        bool lastSitting;

        // Sitting at a camp is a scenario, not always a seat, so either counts.
        static bool PlayerSitting(Ped player)
            => !player.IsOnMount && (player.IsSitting || PED.IS_PED_USING_ANY_SCENARIO(player.Handle));

        void LogFireCheck(Ped player, bool sitting)
        {
            Vector3 fire;
            bool found;
            unsafe { found = FIRE.GET_CLOSEST_FIRE_POS(&fire, player.Position); }
            Log($"seated={sitting} (IsSitting={player.IsSitting}, scenario={PED.IS_PED_USING_ANY_SCENARIO(player.Handle)}) "
                + $"closest fire={(found ? fire.DistanceTo(player.Position).ToString("0.0") + " m" : "none")} "
                + $"fires within {FireRangeMeters} m={FIRE.GET_NUMBER_OF_FIRES_IN_RANGE(player.Position, FireRangeMeters)} "
                + $"interior={INTERIOR.GET_INTERIOR_FROM_ENTITY(player.Handle)}");
        }

        static string DrunkBracket(float d) => d <= 0.1f ? "sober" : d < 0.4f ? "tipsy" : d < 0.75f ? "drunk" : "hammered";

        void UpdateFishing(Ped player)
        {
            var current = player.Weapons.Current;
            bool rodOut = current != null && (uint)current.Hash == Rod && !player.IsOnMount;
            if (rodOut && !fishing)
            {
                // Walk to a spot beside the player first; the fishing scenario brings its own rod.
                fishing = true;
                castLine = false;
                fishingSince = DateTime.Now;
                fishingSpot = player.Position + player.RightVector * 4f;
                kit.Task.ClearTasks(false);
                if (kit.IsOnMount) kit.Task.DismountAnimal((eEnterExitVehicleFlags)0);
                else kit.Task.GoTo(fishingSpot, 1f, 15000);
                currentAction = "fishing";
                AddEvent("the player got out a fishing rod, and the companion started fishing next to them");
                pendingEvent = "fishing_started";
                Log("fishing with the player");
            }
            else if (fishing && !castLine)
            {
                if (!kit.IsOnMount && kit.Position.DistanceTo(fishingSpot) > 1.5f && (DateTime.Now - fishingSince).TotalSeconds < 12)
                {
                    if (!kit.IsWalking && !kit.IsRunning) kit.Task.GoTo(fishingSpot, 1f, 15000);
                    return;
                }
                kit.Task.ClearTasks(true);
                TASK.TASK_START_SCENARIO_IN_PLACE_HASH(kit.Handle, FishingScenario, -1, true, FishingMaleA, player.Heading, false);
                castLine = true;
                Log("cast his line");
            }
            if (!rodOut && fishing)
            {
                fishing = false;
                kit.Task.ClearTasks(false);
                // He tossed his rod in the lake once; it was too good to lose. Hand him one so the
                // weapon swap below can do it again.
                kit.Weapons.Give(Rod, 1);
                kit.Weapons.Select(Revolver, true);
                AddEvent("the player put the fishing rod away");
                ApplyAction("follow");
            }
        }

        // ---------- people around ----------

        void UpdatePeople(Ped player)
        {
            if (fishing || pendingEvent != null || (DateTime.Now - lastPeopleEvent).TotalSeconds < PeopleCommentSeconds) return;
            foreach (var old in seenPeople.Where(kv => (DateTime.Now - kv.Value).TotalMinutes > PeopleForgetMinutes).Select(kv => kv.Key).ToList())
                seenPeople.Remove(old);

            var someone = World.GetAllPeds()
                .Where(p => p != null && p.Exists() && !p.IsDead && p.IsHuman
                    && p.Handle != player.Handle && p.Handle != kit.Handle
                    && !seenPeople.ContainsKey(p.Handle)
                    && p.Position.DistanceTo(player.Position) < 15f)
                .OrderBy(p => Notable(p) ? 0 : 1)
                .ThenBy(p => p.Position.DistanceTo(player.Position))
                .FirstOrDefault();
            if (someone == null) return;
            seenPeople[someone.Handle] = DateTime.Now;
            if (!Notable(someone) && seenPeople.Count % 3 != 0) return;   // ordinary folk: only now and then

            lastPeopleEvent = DateTime.Now;
            AddEvent("passed " + Describe(someone) + " who is " + Doing(someone));
            pendingEvent = "people_nearby";
        }

        bool Notable(Ped p)
        {
            string d = Describe(p);
            return p.IsDrunk || p.IsUsingScenario || d.Contains("lawman") || d.Contains("gang")
                || d.Contains("well-dressed") || d.Contains("odd") || d.Contains("preacher")
                || d.Contains("gambler") || d.Contains("doctor") || d.Contains("saloon");
        }

        // A plain description from the character model's name, never a story character's name.
        static readonly (string Token, string Word)[] Roles =
        {
            ("sheriff", "lawman"), ("deputy", "lawman"), ("lawman", "lawman"), ("marshal", "lawman"),
            ("police", "lawman"), ("pinkerton", "agent in a bowler hat"), ("army", "soldier"),
            ("soldier", "soldier"), ("drunk", "drunk"), ("farmer", "farmer"), ("rancher", "rancher"),
            ("cowboy", "cowboy"), ("miner", "miner"), ("hunter", "hunter"), ("trapper", "trapper"),
            ("doctor", "doctor"), ("priest", "preacher"), ("preacher", "preacher"), ("nun", "nun"),
            ("banker", "banker"), ("gambler", "gambler"), ("bartender", "bartender"),
            ("barber", "barber"), ("blacksmith", "blacksmith"), ("butcher", "butcher"),
            ("laborer", "laborer"), ("dockworker", "dockworker"), ("sailor", "sailor"),
            ("vagrant", "drifter"), ("beggar", "beggar"), ("prostitute", "saloon girl"),
            ("upperclass", "well-dressed"), ("rich", "well-dressed"), ("fancy", "well-dressed"),
            ("lowerclass", "rough-looking"), ("rough", "rough-looking"), ("townfolk", "townsfolk"),
            ("traveller", "traveler"), ("traveler", "traveler"),
        };

        string Describe(Ped p)
        {
            string model = (Enum.GetName(typeof(PedHash), Enum.ToObject(typeof(PedHash), p.Model.Hash)) ?? "").ToLowerInvariant();
            if (p.IsAnimal)
            {
                string animal = model.StartsWith("a_c_") ? model.Substring(4).Split('_')[0] : "animal";
                return "a " + animal;
            }
            string who = p.Gender == Gender.Female ? "woman" : "man";
            if (model.StartsWith("g_")) return "a gang member";
            if (model.StartsWith("cs_") || model.StartsWith("player_") || model.StartsWith("mp_")) return "a local " + who;
            if (model.StartsWith("u_")) return "an odd local " + who;
            var role = Roles.FirstOrDefault(r => model.Contains(r.Token)).Word;
            if (role == null) return "a " + (p.Gender == Gender.Female ? "townswoman" : "townsman");
            string article = "aeiou".Contains(role[0]) ? "an " : "a ";
            return role == "townsfolk" || role == "well-dressed" || role == "rough-looking"
                ? article + role + " " + who
                : article + role + (p.Gender == Gender.Female && role != "nun" && role != "saloon girl" ? " (a woman)" : "");
        }

        string Doing(Ped p)
        {
            if (p.IsDead) return "dead";
            if (p.IsInCombat) return "fighting";
            if (p.IsFleeing) return "running away";
            if (p.IsCowering) return "cowering";
            if (p.IsDrunk) return "drunk";
            if (p.IsSitting) return "sitting around";
            if (p.IsUsingScenario) return "busy with something";
            if (p.IsRunning) return "running";
            return p.Speed > 0.5f ? "walking by" : "standing around";
        }
    }
}
