// A filming aid: one key spawns a small band of armed outlaws about 40 m ahead who come for the
// player, so a gunfight is there when you want one. Use it out in the wilds, not in town.

using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;

namespace DaveCompanion
{
    public partial class Companion
    {
        const int AmbushSize = 4;
        const float AmbushMeters = 40f;
        const int AmbushTimeoutSeconds = 10;
        static readonly PedHash[] AmbushModels =
            { PedHash.g_m_m_unicriminals_01, PedHash.g_m_m_unicriminals_02, PedHash.g_m_m_unibanditos_01 };

        bool ambushLoading;
        DateTime ambushRequested;
        PedHash ambushModel;
        readonly List<Ped> ambushers = new List<Ped>();
        readonly Random rng = new Random();

        void StartAmbush()
        {
            if (ambushLoading) return;
            ambushModel = AmbushModels[rng.Next(AmbushModels.Length)];
            STREAMING.REQUEST_MODEL((uint)ambushModel, false);
            ambushRequested = DateTime.Now;
            ambushLoading = true;
        }

        void UpdateAmbush()
        {
            if (ambushLoading)
            {
                if (STREAMING.HAS_MODEL_LOADED((uint)ambushModel)) { ambushLoading = false; SpawnAmbush(); }
                else if ((DateTime.Now - ambushRequested).TotalSeconds > AmbushTimeoutSeconds)
                {
                    ambushLoading = false;
                    Log("ambush: model " + ambushModel + " did not load");
                }
                return;
            }
            // Once they're down, hand the bodies back to the game to clean up.
            for (int i = ambushers.Count - 1; i >= 0; i--)
            {
                Ped p = ambushers[i];
                if (p == null || !p.Exists() || p.IsDead)
                {
                    if (p != null && p.Exists()) { int h = p.Handle; unsafe { ENTITY.SET_PED_AS_NO_LONGER_NEEDED(&h); } }
                    ambushers.RemoveAt(i);
                }
            }
        }

        void SpawnAmbush()
        {
            try
            {
                Ped player = Game.Player.Character;
                uint group = World.AddRelationshipGroup("KIT_AMBUSH");
                uint playerGroup = (uint)Game.Joaat("PLAYER");
                World.SetRelationshipBetweenGroups(eRelationshipType.Hate, group, playerGroup);
                World.SetRelationshipBetweenGroups(eRelationshipType.Hate, playerGroup, group);

                Vector3 centre = player.Position + player.ForwardVector * AmbushMeters;
                for (int i = 0; i < AmbushSize; i++)
                {
                    Vector3 at = centre + player.RightVector * ((i - (AmbushSize - 1) / 2f) * 4f);
                    at.Z = World.GetGroundZ(at) + 1f;
                    Ped p = World.CreatePed(ambushModel, at, player.Heading + 180f);
                    if (p == null) continue;
                    ENTITY.SET_ENTITY_AS_MISSION_ENTITY(p.Handle, true, true);
                    PED._SET_RANDOM_OUTFIT_VARIATION(p.Handle, true);
                    p.Weapons.Give(i % 2 == 0 ? Repeater : Revolver, 200);
                    p.Accuracy = 20;  // a fair fight for the camera, not a massacre
                    PED.SET_PED_RELATIONSHIP_GROUP_HASH(p.Handle, group);
                    PED.SET_PED_COMBAT_ATTRIBUTES(p.Handle, 46, true);  // always fight, never flee
                    TASK.TASK_COMBAT_PED(p.Handle, player.Handle, 0, 16);
                    ambushers.Add(p);
                }
                STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED((uint)ambushModel);
                Log($"ambush: {ambushers.Count} {ambushModel} spawned {AmbushMeters} m ahead");
                Notice("Riders ahead. They don't look friendly.");
            }
            catch (Exception ex) { Log("ambush failed: " + ex.Message); }
        }
    }
}
