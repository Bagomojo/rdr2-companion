// Kit Dorsey, a Claude-driven companion for RDR2 story mode.
// Milestone 2: spawn Kit and his horse, follow the player, send snapshots to the
// local bridge, and show Kit's lines as subtitles. Only follow and wait_here are
// carried out so far; every other action falls back to follow until milestone 4.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using Screen = RDR2.UI.Screen;

namespace DaveCompanion
{
    public partial class Companion : Script
    {
        const string BridgeUrl = "http://127.0.0.1:7777/decide";
        const int HeartbeatSeconds = 20;
        const float FollowRepathMeters = 25f;
        const float CatchUpMeters = 150f;
        const float OffScreenCatchUpMeters = 45f;
        const float FarBehindMeters = 15f;
        const float WalkToHorseMeters = 20f;   // closer than this, he goes to his horse instead of whistling
        const float HorseWhistleMeters = 100f; // your whistle brings his horse too, from this close
        const int HorseWaitSeconds = 10;       // a whistled horse that hasn't come by then is brought up off screen

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };  // campfire stories take Opus up to 15 s
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly ConcurrentQueue<Dictionary<string, object>> decisions = new ConcurrentQueue<Dictionary<string, object>>();
        readonly List<string> recentEvents = new List<string>();
        readonly List<string> recentLines = new List<string>();

        Keys keySummon = Keys.NumPad1, keyThink = Keys.NumPad2, keyWait = Keys.NumPad3, keyFollow = Keys.NumPad4, keyScout = Keys.NumPad5, keyClose = Keys.NumPad6, keyAmbush = Keys.NumPad7, keyEditor = Keys.End;

        // Once a look has been saved, the companion is built on the character-creator body.
        uint KitModel => lookEnabled ? (uint)PedHash.mp_male : (uint)PedHash.a_m_m_strtownfolk_01;
        const uint RoachModel = (uint)PedHash.a_c_horse_missourifoxtrotter_dapplegrey;
        readonly Look look = new Look();
        string lookIni;
        bool lookEnabled, editing, openEditorAfterSpawn;
        int editPart;
        // Which companion: a folder in the bridge's companions folder, and looks\<id>.ini here.
        // Set with Companion= in DaveCompanion.ini.
        string companionId = "dave";
        // Shown on his subtitles and notices: Name= in DaveCompanion.ini, else the companion's own
        // companion.ini in the bridge folder, else the id.
        string name = "";
        const int SpawnTimeoutSeconds = 10;

        // Key presses are only recorded here; all game calls happen on the tick,
        // because a native call that waits inside the key handler stalls the script.
        readonly ConcurrentQueue<string> keyQueue = new ConcurrentQueue<string>();

        Ped kit, roach;
        bool summoned, dismissedForMission, requestInFlight, spawning;
        DateTime spawnStarted, refreshLooksAt = DateTime.MaxValue;
        string currentAction = "follow";
        string pendingHotkey;
        bool followFar, followInside, followLagging, sideBySide;
        DateTime lastSent = DateTime.MinValue, lastFollowCheck = DateTime.MinValue, subtitleUntil = DateTime.MinValue;
        string subtitle = "";
        bool lastPlayerInCombat, lastPlayerMounted, lastMission;
        int lastKitHealth;
        const int DownSeconds = 8;
        DateTime downAt = DateTime.MaxValue;
        string nextSpawnEvent = "summoned";
        // A hello the bridge missed because it was still starting (reading keys can take a minute),
        // sent again every few seconds until it answers.
        string retryEvent;
        DateTime retryUntil;
        bool toldBridgeDown;
        const int BridgeRetrySeconds = 5, BridgeRetryForSeconds = 180;
        bool holdingBack, followMounted, followFast;

        public Companion()
        {
            LoadKeys();
            if (name.Length == 0) name = CompanionName();
            StartBridge();
            lookIni = LookPath();
            lookEnabled = File.Exists(lookIni);
            try { look.Load(lookIni); } catch (Exception ex) { Log("look: " + ex.Message); }
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (s, e) => Despawn();
            Interval = 0;
            Log($"loaded v{GetType().Assembly.GetName().Version.ToString(3)}, companion {companionId}, bridge {(bridgeDir.Length > 0 ? "starts with the game" : "started by hand")}");
        }

        // ---------- input ----------

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == keySummon) keyQueue.Enqueue("summon");
            else if (e.KeyCode == keyThink) keyQueue.Enqueue("what_do_you_think");
            else if (e.KeyCode == keyWait) keyQueue.Enqueue("wait_here");
            else if (e.KeyCode == keyFollow) keyQueue.Enqueue("follow_me");
            else if (e.KeyCode == keyScout) keyQueue.Enqueue("scout_ahead");
            else if (e.KeyCode == keyClose) keyQueue.Enqueue("side_by_side");
            else if (e.KeyCode == keyAmbush) keyQueue.Enqueue("ambush");
            else if (e.KeyCode == keyEditor) keyQueue.Enqueue("editor");
            else if (editing)
            {
                string big = e.Shift ? "+" : "";
                if (e.KeyCode == Keys.Up) keyQueue.Enqueue("ed_up");
                else if (e.KeyCode == Keys.Down) keyQueue.Enqueue("ed_down");
                else if (e.KeyCode == Keys.Left) keyQueue.Enqueue("ed_left" + big);
                else if (e.KeyCode == Keys.Right) keyQueue.Enqueue("ed_right" + big);
            }
        }

        void HandleKeys()
        {
            while (keyQueue.TryDequeue(out var key))
            {
                if (key == "summon")
                {
                    if (editing) StopEditing();
                    if (summoned) { summoned = false; spawning = false; Despawn(); Notice(name + " rides off for now."); }
                    else { summoned = true; BeginSpawn(); }
                }
                else if (key == "editor") ToggleEditor();
                else if (key == "ambush") StartAmbush();
                else if (key == "side_by_side")
                {
                    // For filming two-shots: he rides and walks right beside the player.
                    sideBySide = !sideBySide;
                    Log("side by side " + (sideBySide ? "on" : "off"));
                    Notice(sideBySide ? name + " sticks right beside you." : name + " gives you some room.");
                    if (summoned && Alive(kit) && currentAction == "follow") { holdingBack = false; ApplyAction("follow"); }
                }
                else if (key.StartsWith("ed_")) EditorKey(key);
                else if (summoned && Alive(kit) && !editing) Hotkey(key);
            }
        }

        void Hotkey(string name)
        {
            // Obey the simple orders at once; Kit's reply arrives with the decision.
            if (name == "wait_here") ApplyAction("wait_here");
            if (name == "follow_me") ApplyAction("follow");
            AddEvent("player pressed " + name);
            pendingHotkey = name;
        }

        // ---------- main loop ----------

        void OnTick(object sender, EventArgs e)
        {
            HandleKeys();
            DrainDecisions();
            UpdateAmbush();
            if (DateTime.Now < subtitleUntil) Screen.DisplaySubtitle(subtitle);

            if (!summoned) return;
            if (spawning) { SpawnStep(); return; }
            if (DateTime.Now >= refreshLooksAt) { refreshLooksAt = DateTime.MaxValue; CheckLooks(); }
            Ped player = Game.Player.Character;

            bool mission = Game.IsMissionActive;
            if (mission && !lastMission && Alive(kit))
            {
                Despawn();
                dismissedForMission = true;
                Notice(name + " hangs back until you're done.");
            }
            if (!mission && lastMission && dismissedForMission)
            {
                dismissedForMission = false;
                BeginSpawn();
            }
            lastMission = mission;
            if (mission) return;
            if (!Alive(kit)) { HandleDown(player); return; }
            if (openEditorAfterSpawn) { openEditorAfterSpawn = false; StartEditing(); }
            if (editing) { EditorTick(); return; }

            if (PlayerWhistled()) CallHorseToo();
            string trigger = DetectEvent(player);
            if (trigger == null && pendingEvent != null && !requestInFlight) { trigger = pendingEvent; pendingEvent = null; }
            if (pendingHotkey != null && !requestInFlight) trigger = "hotkey";
            else if (trigger == null && retryEvent != null && (DateTime.Now - lastSent).TotalSeconds >= BridgeRetrySeconds)
            {
                if (DateTime.Now < retryUntil) trigger = retryEvent;
                else retryEvent = null;
            }
            else if (trigger == null && (DateTime.Now - lastSent).TotalSeconds >= HeartbeatSeconds) trigger = "heartbeat";
            if (trigger != null && !requestInFlight) Send(trigger, player);

            if ((DateTime.Now - lastFollowCheck).TotalSeconds >= 0.5)
            {
                lastFollowCheck = DateTime.Now;
                UpdateActivities(player);
                // Fighting, fishing and scouting each steer him themselves.
                if (!combatActive && !fishing && !atFire && !atBar && !Scouting) KeepFollowing(player);
            }
        }

        string DetectEvent(Ped player)
        {
            string evt = null;
            bool inCombat = player.IsInCombat;
            if (inCombat && !lastPlayerInCombat) { evt = "combat_start"; AddEvent("a fight started"); }
            else if (!inCombat && lastPlayerInCombat) { evt = "combat_end"; AddEvent("the fight ended"); }
            lastPlayerInCombat = inCombat;

            bool mounted = player.IsOnMount;
            if (mounted != lastPlayerMounted) AddEvent(mounted ? "player mounted up" : "player got down from the horse");
            lastPlayerMounted = mounted;

            int hp = kit.Health;
            if (lastKitHealth > 0 && hp < lastKitHealth - kit.MaxHealth / 4) { evt = evt ?? "companion_hurt"; AddEvent("the companion was hurt"); }
            lastKitHealth = hp;
            return evt;
        }

        // ---------- spawning and movement ----------

        void BeginSpawn()
        {
            kitDrunk = 0f;  // a fresh ped starts sober; UpdateDrinking catches him up
            // Ask the game to stream both models in; SpawnStep checks each tick until they're ready.
            STREAMING.REQUEST_MODEL(KitModel, false);
            STREAMING.REQUEST_MODEL(RoachModel, false);
            spawning = true;
            spawnStarted = DateTime.Now;
        }

        void SpawnStep()
        {
            if (STREAMING.HAS_MODEL_LOADED(KitModel) && STREAMING.HAS_MODEL_LOADED(RoachModel))
            {
                spawning = false;
                Spawn();
                STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(KitModel);
                STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(RoachModel);
            }
            else if ((DateTime.Now - spawnStarted).TotalSeconds > SpawnTimeoutSeconds)
            {
                spawning = false;
                summoned = false;
                Log("spawn failed: models did not load within " + SpawnTimeoutSeconds + "s");
                Notice(name + " couldn't be spawned. See DaveCompanion.log.");
            }
        }

        void Spawn()
        {
            try
            {
                Ped player = Game.Player.Character;
                Vector3 at = player.Position + player.RightVector * 3f - player.ForwardVector * 2f;

                roach = World.CreatePed(PedHash.a_c_horse_missourifoxtrotter_dapplegrey, at + player.RightVector * 2f, player.Heading);
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(roach.Handle, true, true);
                // A random variation can leave a horse half-built; a full outfit preset gives the whole body.
                int presets = PED.GET_NUM_META_PED_OUTFITS(roach.Handle);
                if (presets > 0) PED._EQUIP_META_PED_OUTFIT_PRESET(roach.Handle, 0, false);
                else PED._SET_RANDOM_OUTFIT_VARIATION(roach.Handle, true);
                Log("horse created, outfit presets: " + presets);

                kit = World.CreatePed((PedHash)KitModel, at, player.Heading);
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(kit.Handle, true, true);
                if (lookEnabled) look.Apply(kit);
                // A fixed preset keeps Kit's face and clothes the same on every summon.
                else if (PED.GET_NUM_META_PED_OUTFITS(kit.Handle) > 0) PED._EQUIP_META_PED_OUTFIT_PRESET(kit.Handle, 0, false);
                else PED._SET_RANDOM_OUTFIT_VARIATION(kit.Handle, true);
                look.ApplyHorse(roach);
                RefreshLooks();
                lookTries = 0;
                refreshLooksAt = DateTime.Now.AddSeconds(1.5);
                SetUpKit();

                if (player.IsOnMount) PED.SET_PED_ONTO_MOUNT(kit.Handle, roach.Handle, -1, true);
                lastKitHealth = kit.Health;
                currentAction = "follow";
                ApplyAction("follow");
                bool recovering = nextSpawnEvent == "companion_recovered";
                AddEvent(recovering ? "the companion got back up and caught up, still hurting" : "the companion joined the player");
                Send(nextSpawnEvent, player);
                nextSpawnEvent = "summoned";
                Log(recovering ? "respawned after going down" : "spawned");
            }
            catch (Exception ex)
            {
                // No game calls here: if the script is being aborted, they would throw again.
                Log("spawn failed: " + ex);
                summoned = false;
                Notice(name + " couldn't be spawned. See DaveCompanion.log.");
            }
        }

        void SetUpKit()
        {
            kit.BlockPermanentEvents = true;
            int group = PLAYER.GET_PLAYER_GROUP(Game.Player.Handle);
            PED.SET_PED_AS_GROUP_MEMBER(kit.Handle, group);
            // Group membership keeps him on the player's side in fights, but the default
            // formation pulls members in tight; widen it so it doesn't fight our follow spot.
            PED.SET_GROUP_FORMATION_SPACING(group, 5f, -1f, -1f);
            PED.SET_PED_KEEP_TASK(kit.Handle, true);
            ArmKit();
            // Riding into trees killed him: keep him in the saddle and immune to impact damage.
            // Bullets, blades and fire still hurt him.
            PED.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE(kit.Handle, 1);
            kit.IsCollisionProof = true;
            if (roach != null && roach.Exists()) roach.IsCollisionProof = true;
        }

        void HandleDown(Ped player)
        {
            // Kit is too important to lose for good: when he goes down he gets back up,
            // once nobody nearby is still fighting.
            if (downAt == DateTime.MaxValue)
            {
                downAt = DateTime.Now;
                AddEvent("the companion went down");
                Notice(name + " is down.");
                Log("kit went down");
                return;
            }
            if ((DateTime.Now - downAt).TotalSeconds < DownSeconds || player.IsInCombat) return;

            downAt = DateTime.MaxValue;
            if (kit != null && kit.Exists() && roach != null && roach.Exists() && !roach.IsDead)
            {
                kit.Resurrect();
                kit.Health = kit.MaxHealth / 2;
                SetUpKit();
                lastKitHealth = kit.Health;
                currentAction = "follow";
                ApplyAction("follow");
                AddEvent("the companion got back up, still hurting");
                Send("companion_recovered", player);
                Log("kit got back up");
            }
            else
            {
                // Roach is gone too, or the game cleaned Kit up: bring them both back fresh.
                Despawn();
                nextSpawnEvent = "companion_recovered";
                BeginSpawn();
            }
        }

        void RefreshLooks()
        {
            // Applies the chosen outfit pieces. Run at spawn and again once the peds have
            // finished streaming in, since the first pass can land before they're ready.
            if (roach != null && roach.Exists()) look.ApplyHorse(roach);
            if (kit != null && kit.Exists())
            {
                if (lookEnabled) look.Apply(kit);
                else PED._UPDATE_PED_VARIATION(kit.Handle, false, true, true, true, false);
            }
        }

        // In Valentine a freshly saved look (new head and skin tone) hadn't streamed in by the one
        // re-apply at 1.5 s, and he talked from an invisible body. Keep re-applying until the game
        // says he's ready to render and his head and body are really on him.
        int lookTries;
        const int MaxLookTries = 10;

        void CheckLooks()
        {
            if (kit == null || !kit.Exists()) return;
            if (!lookEnabled) { RefreshLooks(); return; }
            // The first check always re-applies: the pass at spawn usually lands before streaming.
            bool ready = lookTries > 0 && PED.IS_PED_READY_TO_RENDER(kit.Handle)
                && look.Applied.All(h => PED._IS_METAPED_USING_COMPONENT(kit.Handle, h));
            if (ready) { if (lookTries > 1) Log("look: ready after " + lookTries + " tries"); return; }
            if (lookTries >= MaxLookTries)
            {
                Log($"look: gave up after {lookTries} tries (ready to render={PED.IS_PED_READY_TO_RENDER(kit.Handle)}, "
                    + $"{look.Applied.Count(h => PED._IS_METAPED_USING_COMPONENT(kit.Handle, h))} of {look.Applied.Count} head/body pieces on)");
                return;
            }
            RefreshLooks();
            lookTries++;
            refreshLooksAt = DateTime.Now.AddSeconds(1.5);
        }

        // ---------- look editor ----------

        void ToggleEditor()
        {
            if (editing) { StopEditing(); return; }
            if (!summoned || !Alive(kit)) { Notice("Summon " + name + " first, then press " + keyEditor + " to edit his look."); return; }
            if (!lookEnabled)
            {
                // First use: switch him to the character-creator body, then open the editor.
                lookEnabled = true;
                look.Save(lookIni);
                Despawn();
                openEditorAfterSpawn = true;
                BeginSpawn();
                return;
            }
            StartEditing();
        }

        void StartEditing()
        {
            Ped player = Game.Player.Character;
            if (player.IsOnMount || kit.IsOnMount) { Notice("Get off your horses to edit his look."); return; }
            editing = true;
            currentAction = "editing";
            kit.Task.ClearTasks(true);
            // Stand him close in front of the camera, facing it, so his face fills the frame.
            Vector3 cam = GameplayCamera.Position;
            Vector3 fwd = GameplayCamera.ForwardVector;
            float len = (float)Math.Sqrt(fwd.X * fwd.X + fwd.Y * fwd.Y);
            if (len < 0.01f) { fwd = player.ForwardVector; len = 1f; }
            var spot = new Vector3(cam.X + fwd.X / len * 2.3f, cam.Y + fwd.Y / len * 2.3f, player.Position.Z);
            kit.Position = spot;
            kit.Heading = (float)(Math.Atan2(-(cam.X - spot.X), cam.Y - spot.Y) * 180.0 / Math.PI);
            kit.Task.StandStill(-1);
            Log("look editor opened");
        }

        void StopEditing()
        {
            editing = false;
            try { look.Save(lookIni); } catch (Exception ex) { Log("look save: " + ex.Message); }
            Notice("Look saved.");
            Log("look editor closed, saved");
            if (Alive(kit)) ApplyAction("follow");
        }

        void EditorKey(string key)
        {
            if (!editing || !Alive(kit)) return;
            var parts = look.Parts;
            if (key == "ed_up") editPart = (editPart + parts.Count - 1) % parts.Count;
            else if (key == "ed_down") editPart = (editPart + 1) % parts.Count;
            else
            {
                var p = parts[editPart];
                int step = key.EndsWith("+") ? 10 : 1;
                int dir = key.StartsWith("ed_left") ? -step : step;
                p.Value = ((p.Value + dir) % p.Count + p.Count) % p.Count;
                look.Apply(kit);
                if (roach != null && roach.Exists()) look.ApplyHorse(roach);
            }
        }

        void EditorTick()
        {
            // Keep the player still so the arrow keys only drive the editor.
            Game.DisableAllControlsThisFrame();
            var p = look.Parts[editPart];
            Screen.DisplaySubtitle($"{p.Label} ({editPart + 1}/{look.Parts.Count}): {p.Describe(p.Value)}   " +
                $"[Up/Down part, Left/Right change, Shift jumps 10, {keyEditor} saves]");
        }

        // The game's whistle, on foot or riding.
        static bool PlayerWhistled()
            => Function.Call<bool>(0x91AEF906BCA88877, 0, 0x24978A28u)   // IS_CONTROL_JUST_PRESSED, INPUT_WHISTLE
            || Function.Call<bool>(0x91AEF906BCA88877, 0, 0xE7EB9185u);  // INPUT_WHISTLE_HORSEBACK

        // When you whistle for your horse, his comes too if it's in earshot and he's on foot.
        void CallHorseToo()
        {
            if (kit.IsOnMount || roach == null || !roach.Exists() || roach.IsDead) return;
            float toRoach = roach.Position.DistanceTo(kit.Position);
            if (toRoach < 6f || toRoach > HorseWhistleMeters) return;
            roach.Task.GoToWhistle(kit);
            lastWhistle = DateTime.Now;
            Log($"you whistled: his horse comes too ({toRoach:0} m)");
        }

        void Notice(string text)
        {
            // Shown by the tick, like Kit's own lines.
            subtitle = text;
            subtitleUntil = DateTime.Now.AddSeconds(4);
        }

        void Despawn()
        {
            try
            {
                DropBottle();
                if (Alive(kit) || (kit != null && kit.Exists())) kit.Delete();
                if (roach != null && roach.Exists()) roach.Delete();
            }
            catch (Exception ex) { Log("despawn: " + ex.Message); }
            kit = null; roach = null;
            downAt = DateTime.MaxValue;
        }

        void KeepFollowing(Ped player)
        {
            if (currentAction != "follow") return;
            float dist = kit.Position.DistanceTo(player.Position);

            // Far behind and off screen, or very far: catch up quietly. Past 40 m he can't be heard.
            if (dist > CatchUpMeters || (dist > OffScreenCatchUpMeters && !kit.IsOnScreen))
            {
                Vector3 behind = player.Position - player.ForwardVector * 12f;
                if (roach != null && roach.Exists()) roach.Position = behind + player.RightVector * 2f;
                kit.Position = behind;
                if (player.IsOnMount && roach != null) PED.SET_PED_ONTO_MOUNT(kit.Handle, roach.Handle, -1, true);
                ApplyAction("follow");
                return;
            }
            if (player.IsOnMount && !kit.IsOnMount && roach != null && roach.Exists())
            {
                // Call Roach over instead of walking back for him; keep up on foot meanwhile.
                float toRoach = roach.Position.DistanceTo(kit.Position);
                if (toRoach > 80f)
                {
                    roach.Position = kit.Position - kit.ForwardVector * 3f;
                    toRoach = 3f;
                }
                if (toRoach > WalkToHorseMeters)
                {
                    double since = (DateTime.Now - lastWhistle).TotalSeconds;
                    if (since > HorseWaitSeconds)
                    {
                        if (since < HorseWaitSeconds + 5 && !roach.IsOnScreen)
                        {
                            // Whistled and still not here (stuck on a fence, or lost): bring him up unseen, close
                            // enough that he walks over to him.
                            roach.Position = kit.Position - player.ForwardVector * 15f;  // behind, out of the camera's view
                            Log("horse: didn't come, brought up behind him");
                        }
                        roach.Task.GoToWhistle(kit);
                        lastWhistle = DateTime.Now;
                    }
                    if (dist > FollowRepathMeters) ApplyAction("follow");
                    return;
                }
                // Close enough: he goes to his horse and climbs on rather than chasing you on foot.
                if ((DateTime.Now - lastMountOrder).TotalSeconds > 2)
                {
                    kit.Task.MountAnimal(roach, -1, (eVehicleSeat)(-1), 2f, (eEnterExitVehicleFlags)0);
                    lastMountOrder = DateTime.Now;
                }
                return;
            }
            if (!player.IsOnMount && kit.IsOnMount)
            {
                kit.Task.DismountAnimal((eEnterExitVehicleFlags)0);
                return;
            }
            // On foot outdoors he never stands in the player's path: if he's in front of them, or
            // they walk into him, he steps aside. Never while riding (on a road that made him peel
            // away every time he caught up) and never indoors (it parked him in the doorway).
            bool inside = INTERIOR.GET_INTERIOR_FROM_ENTITY(player.Handle) != 0;
            if (!player.IsOnMount && !kit.IsOnMount && !inside && !sideBySide)
            {
                Vector3 toKit = kit.Position - player.Position;
                float ahead = toKit.X * player.ForwardVector.X + toKit.Y * player.ForwardVector.Y;
                float side = toKit.X * player.RightVector.X + toKit.Y * player.RightVector.Y;
                // Only when he's really in their way: in front and in line, with the player walking.
                // (Any time the player turned round he used to count as "in front" and shuffle off.)
                bool inPath = ahead > 0.5f && Math.Abs(side) < 1.2f && dist < 4f && player.Speed > 0.5f;
                bool crowding = dist < 1.2f && player.Speed > 0.5f;
                if (inPath || crowding)
                {
                    if (!holdingBack)
                    {
                        // His side first, the other side if someone's standing there, else straight back.
                        float away = side >= 0 ? 1f : -1f;
                        Vector3 back = -player.ForwardVector * 1.5f;
                        Vector3 clear = kit.Position + player.RightVector * (away * 3f) + back;
                        if (!SpotClear(clear)) clear = kit.Position - player.RightVector * (away * 3f) + back;
                        if (!SpotClear(clear)) clear = kit.Position - player.ForwardVector * 2.5f;
                        StepTo(clear, "stepped aside");
                    }
                    return;
                }
                if (DateTime.Now < stepUntil) return;
                // Townsfolk get the same courtesy. He used to stand his ground, and in Strawberry a
                // laborer who kept walking into him picked a fight over it.
                if (!holdingBack && (DateTime.Now - lastMakeWay).TotalSeconds > 1)
                {
                    lastMakeWay = DateTime.Now;
                    Ped walker = World.GetAllPeds().FirstOrDefault(p => p != null && p.Exists() && p.IsHuman && !p.IsDead
                        && p.Handle != player.Handle && p.Handle != kit.Handle && p.Speed > 0.5f
                        && p.Position.DistanceTo(kit.Position) < 1.6f
                        && Vector3.Dot(p.ForwardVector, kit.Position - p.Position) > 0f);
                    if (walker != null)
                    {
                        // Off their line, to whichever side of it he's already on.
                        Vector3 across = walker.RightVector;
                        float onSide = Vector3.Dot(kit.Position - walker.Position, across) >= 0 ? 1f : -1f;
                        Vector3 clear = kit.Position + across * (onSide * 1.8f);
                        if (!SpotClear(clear)) clear = kit.Position - walker.ForwardVector * 2f;
                        StepTo(clear, "made way for someone");
                        return;
                    }
                }
            }
            bool fast = player.IsOnMount ? player.CurrentMount != null && player.CurrentMount.Speed > 6f : player.IsRunning || player.IsSprinting;
            bool far = dist > FarBehindMeters;
            // Side by side: a follow task always trails a little, so when he drops more than a
            // couple of meters back he goes one gait faster until he's level again.
            bool lagging = false;
            if (sideBySide && player.Speed > 0.5f)
            {
                Vector3 rel = kit.Position - player.Position;
                float level = rel.X * player.ForwardVector.X + rel.Y * player.ForwardVector.Y;
                lagging = followLagging ? level < -0.5f : level < -2f;
            }
            if (holdingBack || dist > FollowRepathMeters || player.IsOnMount != followMounted || fast != followFast
                || far != followFar || inside != followInside || lagging != followLagging)
            {
                holdingBack = false;
                ApplyAction("follow");
            }
        }

        DateTime lastMakeWay = DateTime.MinValue, stepUntil = DateTime.MinValue;

        // Nobody but him (the player included) within a metre of the spot.
        bool SpotClear(Vector3 at)
        {
            return !World.GetAllPeds().Any(p => p != null && p.Exists() && p.IsHuman && !p.IsDead
                && p.Handle != kit.Handle && p.Position.DistanceTo(at) < 1.2f);
        }

        void StepTo(Vector3 at, string why)
        {
            kit.Task.ClearTasks(false);
            kit.Task.GoTo(at, 1f, 4000);
            holdingBack = true;
            stepUntil = DateTime.Now.AddSeconds(1.5);
            Log(why);
        }

        void ApplyAction(string action, string target = "")
        {
            if (!Alive(kit)) return;
            if (ApplyActivityAction(action, target)) return;
            switch (action)
            {
                case "wait_here":
                    kit.Task.ClearTasks(false);
                    kit.Task.StandStill(-1);
                    currentAction = "wait_here";
                    break;
                default:
                    // follow, plus every action milestone 4 hasn't wired up yet.
                    // A spot behind and a little to the left: a horse length and more when riding,
                    // a few steps on foot, at the player's own pace so he doesn't run up on them.
                    Ped player = Game.Player.Character;
                    followMounted = player.IsOnMount;
                    followFast = followMounted
                        ? player.CurrentMount != null && player.CurrentMount.Speed > 6f
                        : player.IsRunning || player.IsSprinting;
                    followFar = kit.Position.DistanceTo(player.Position) > FarBehindMeters;
                    if (!sideBySide) followLagging = false;
                    else if (player.Speed > 0.5f)
                    {
                        Vector3 rel = kit.Position - player.Position;
                        float level = rel.X * player.ForwardVector.X + rel.Y * player.ForwardVector.Y;
                        followLagging = followLagging ? level < -0.5f : level < -2f;
                    }
                    followInside = INTERIOR.GET_INTERIOR_FROM_ENTITY(player.Handle) != 0;
                    // Indoors a spot further back can be outside the door, so he stays at the player's shoulder.
                    // By default beside and a step back on the left, where he's in shot and not underfoot;
                    // level with the player in side-by-side, with room between the horses so he doesn't drift in.
                    Vector3 offset = sideBySide
                        ? (followMounted ? new Vector3(-3f, 0.5f, 0f) : new Vector3(-1.5f, 0.3f, 0f))
                        : followMounted ? new Vector3(-2.2f, -3.5f, 0f)
                        : followInside ? new Vector3(-0.9f, -1f, 0f) : new Vector3(-1.6f, -1.2f, 0f);
                    // 3 is a gallop or sprint: needed to keep up with a fast horse or close a gap.
                    float pace = followFast || followFar ? (followMounted || followFar ? 3f : 2f) : 1f;
                    if (followLagging) pace = Math.Min(3f, pace + 1f);
                    if (sideBySide) Log($"side by side: pace {pace}{(followLagging ? ", catching up" : "")}");
                    TASK.TASK_FOLLOW_TO_OFFSET_OF_ENTITY(kit.Handle, player.Handle, offset,
                        pace, -1, sideBySide ? 0.8f : 1.5f, true, false, false, false, false, false);
                    currentAction = "follow";
                    if (action != "follow" && action != "idle") Log("action '" + action + "' not wired yet, following instead");
                    break;
            }
        }

        // ---------- talking to the bridge ----------

        void Send(string evt, Ped player)
        {
            var snapshot = BuildSnapshot(evt, player);
            pendingHotkey = null;
            requestInFlight = true;
            lastSent = DateTime.Now;
            string body = json.Serialize(new Dictionary<string, object> { { "snapshot", snapshot } });

            Task.Run(async () =>
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, BridgeUrl)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                    req.Headers.Add("X-Bridge-Token", BridgeToken());
                    var resp = await Http.SendAsync(req);
                    string text = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"bridge said {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    decisions.Enqueue(json.Deserialize<Dictionary<string, object>>(text));
                    if (evt == retryEvent) { Log("bridge: up, " + evt + " delivered"); retryEvent = null; }
                }
                catch (Exception ex)
                {
                    if (retryEvent == null) Log("bridge unreachable: " + ex.GetBaseException().Message);
                    if (evt == "summoned" || evt == "companion_recovered")
                    {
                        if (retryEvent == null) retryUntil = DateTime.Now.AddSeconds(BridgeRetryForSeconds);
                        retryEvent = evt;
                    }
                    if (!toldBridgeDown)
                    {
                        toldBridgeDown = true;
                        Notice(bridgeDir.Length > 0
                            ? name + " is still waking up (the bridge is starting). Give him a minute."
                            : name + " can't talk: the bridge isn't running. Start start-bridge.cmd.");
                    }
                }
                finally { requestInFlight = false; }
            });
        }

        void DrainDecisions()
        {
            while (decisions.TryDequeue(out var d))
            {
                string action = Str(d, "action", "follow");
                string say = Str(d, "say", "");
                string target = Str(d, "target", "");
                if (Alive(kit) && (action != currentAction || action == "attack")) ApplyAction(action, target);
                if (say.Length > 0)
                {
                    subtitle = name + ": " + say;
                    subtitleUntil = DateTime.Now.AddSeconds(4 + say.Length / 18.0);
                    recentLines.Add(say);
                    if (recentLines.Count > 5) recentLines.RemoveAt(0);
                }
            }
        }

        Dictionary<string, object> BuildSnapshot(string evt, Ped player)
        {
            TimeSpan t = World.CurrentDayTime;
            var nearby = World.GetAllPeds()
                .Where(p => p != null && p.Exists() && p.Handle != player.Handle
                    && (kit == null || p.Handle != kit.Handle) && (roach == null || p.Handle != roach.Handle)
                    && (player.CurrentMount == null || p.Handle != player.CurrentMount.Handle))
                .Select(p => new { p, d = p.Position.DistanceTo(player.Position) })
                .Where(x => x.d < 60f)
                .OrderBy(x => x.p.IsHuman ? 0 : 1).ThenBy(x => x.d)
                .Take(5)
                .Select(x => (object)new Dictionary<string, object>
                {
                    { "id", "p" + x.p.Handle },
                    { "kind", Describe(x.p) },
                    { "doing", Doing(x.p) },
                    { "hostile", !x.p.IsDead && x.p.IsInCombatAgainst(player) },
                    { "distance_m", (int)x.d },
                    { "state", x.p.IsDead ? "dead" : x.p.IsFleeing ? "fleeing" : x.p.IsCowering ? "frightened" : "" },
                })
                .ToList();

            return new Dictionary<string, object>
            {
                { "event", evt },
                { "hotkey", pendingHotkey },
                { "world", new Dictionary<string, object>
                    {
                        { "time", string.Format("{0:00}:{1:00}", t.Hours, t.Minutes) },
                        { "weather", World.CurrentWeather.ToString() },
                        { "region", "" },   // zone names come in a later milestone
                        { "town", null },
                        { "story_mission_active", Game.IsMissionActive },
                    } },
                { "player", new Dictionary<string, object>
                    {
                        { "health", Bracket(player.Health, player.MaxHealth) },
                        { "honor", "unknown" },
                        { "wanted", Game.Player.IsWanted },
                        { "mounted", player.IsOnMount },
                        { "weapon_drawn", Game.Player.IsAiming || player.IsShooting },
                        { "cash", Game.Player.Money < 1000 ? "little" : Game.Player.Money < 10000 ? "some" : "plenty" },
                        { "waypoint", null },
                        { "activity", fishing ? "fishing" : combatActive ? "fighting" : atFire ? "at_campfire" : atBar ? "at_bar" : "" },
                        { "drunk", DrunkBracket(playerDrunk) },
                    } },
                { "profile", companionId },
                { "companion", new Dictionary<string, object>
                    {
                        { "health", Bracket(kit.Health, kit.MaxHealth) },
                        { "distance_m", (int)kit.Position.DistanceTo(player.Position) },
                        { "mounted", kit.IsOnMount },
                        { "current_action", currentAction },
                        { "drunk", DrunkBracket(kitDrunk) },
                    } },
                { "nearby", nearby },
                { "recent_events", recentEvents.ToList() },
                { "recent_lines", recentLines.ToList() },
            };
        }

        // ---------- helpers ----------

        static bool Alive(Ped p) => p != null && p.Exists() && !p.IsDead;

        static string Bracket(int hp, int max)
        {
            if (max <= 0) return "unknown";
            double f = (double)hp / max;
            return f > 0.8 ? "full" : f > 0.5 ? "hurt" : f > 0.2 ? "badly hurt" : "critical";
        }

        static string Str(Dictionary<string, object> d, string key, string fallback)
            => d.TryGetValue(key, out var v) && v is string s ? s : fallback;

        void AddEvent(string e)
        {
            recentEvents.Add(e);
            if (recentEvents.Count > 30) recentEvents.RemoveAt(0);
        }

        void LoadKeys()
        {
            // Optional DaveCompanion.ini next to the dll, lines like "Summon=NumPad1". An install
            // from before the rename still has KitCompanion.ini, so that's read when it's the only one.
            try
            {
                string ini = Path.Combine(BaseDirectory, "DaveCompanion.ini");
                if (!File.Exists(ini)) ini = Path.Combine(BaseDirectory, "KitCompanion.ini");
                if (!File.Exists(ini)) return;
                foreach (var line in File.ReadAllLines(ini))
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2 && parts[0].Trim().Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts[1].Trim().Length > 0) name = parts[1].Trim();
                        continue;
                    }
                    if (parts.Length == 2 && parts[0].Trim().Equals("Companion", StringComparison.OrdinalIgnoreCase))
                    {
                        // It names a folder and a file, so only letters, digits, - and _ (no ..\).
                        string id = parts[1].Trim().ToLowerInvariant();
                        if (Regex.IsMatch(id, "^[a-z0-9_-]{1,40}$")) companionId = id;
                        else if (id.Length > 0) Log("ini: Companion=" + id + " isn't a folder name; using " + companionId);
                        continue;
                    }
                    if (parts.Length == 2 && parts[0].Trim().Equals("Bridge", StringComparison.OrdinalIgnoreCase))
                    {
                        bridgeDir = parts[1].Trim();
                        continue;
                    }
                    if (parts.Length != 2 || !Enum.TryParse(parts[1].Trim(), true, out Keys k)) continue;
                    switch (parts[0].Trim().ToLowerInvariant())
                    {
                        case "summon": keySummon = k; break;
                        case "whatdoyouthink": keyThink = k; break;
                        case "waithere": keyWait = k; break;
                        case "followme": keyFollow = k; break;
                        case "scoutahead": keyScout = k; break;
                        case "sidebyside": keyClose = k; break;
                        case "ambush": keyAmbush = k; break;
                        case "lookeditor": keyEditor = k; break;
                    }
                }
            }
            catch (Exception ex) { Log("ini: " + ex.Message); }
        }

        // looks\<companion>.ini, written by the look editor. A Dave install from before companions
        // had one look in KitLook.ini; it's used until the editor saves his own file.
        string LookPath()
        {
            string own = Path.Combine(BaseDirectory, "looks", companionId + ".ini");
            string old = Path.Combine(BaseDirectory, "KitLook.ini");
            return !File.Exists(own) && companionId == "dave" && File.Exists(old) ? old : own;
        }

        // name = in the companion's companion.ini, when the bridge folder is known.
        string CompanionName()
        {
            try
            {
                string ini = Path.Combine(bridgeDir, "companions", companionId, "companion.ini");
                if (bridgeDir.Length > 0 && File.Exists(ini))
                    foreach (var line in File.ReadAllLines(ini))
                    {
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals("name", StringComparison.OrdinalIgnoreCase)
                            && parts[1].Trim().Length > 0)
                            return parts[1].Split(';')[0].Trim();
                    }
            }
            catch (Exception ex) { Log("companion.ini: " + ex.Message); }
            return companionId.Length > 0 ? char.ToUpperInvariant(companionId[0]) + companionId.Substring(1) : "Dave";
        }

        // Bridge= in DaveCompanion.ini: the bridge folder. When it's set, the bridge starts with the
        // game (hidden, its output in logs\console.txt) and quits when the game does.
        string bridgeDir = "";

        void StartBridge()
        {
            if (bridgeDir.Length == 0) return;
            string cmd = Path.Combine(bridgeDir, "start-bridge.cmd");
            if (!File.Exists(cmd)) { Log("bridge: no start-bridge.cmd in " + bridgeDir); return; }
            try
            {
                using (var probe = new TcpClient())
                    if (probe.ConnectAsync("127.0.0.1", 7777).Wait(300)) { Log("bridge: already running"); return; }
            }
            catch { }  // refused: nothing listening yet
            try
            {
                string logs = Path.Combine(bridgeDir, "logs");
                Directory.CreateDirectory(logs);
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c \"\"{cmd}\" > \"{Path.Combine(logs, "console.txt")}\" 2>&1\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = bridgeDir,
                };
                info.EnvironmentVariables["RDR2_GAME_PID"] = Process.GetCurrentProcess().Id.ToString();
                info.EnvironmentVariables["RDR2_COMPANION"] = companionId;
                Process.Start(info);
                Log("bridge: started");
            }
            catch (Exception ex) { Log("bridge: couldn't start: " + ex.Message); }
        }

        // The bridge writes a new secret here each time it starts; requests without it are refused.
        static readonly string TokenFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaveCompanion", "bridge-token");

        // The bridge also keeps a copy in its state folder; whichever was written last is the live one.
        string BridgeToken()
        {
            try
            {
                string own = bridgeDir.Length > 0 ? Path.Combine(bridgeDir, "state", "bridge-token") : "";
                string newest = new[] { TokenFile, own }.Where(p => p.Length > 0 && File.Exists(p))
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                return newest == null ? "" : File.ReadAllText(newest).Trim();
            }
            catch { return ""; }
        }

        void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(BaseDirectory, "DaveCompanion.log"), $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); }
            catch { }
        }
    }
}
