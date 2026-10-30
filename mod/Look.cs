// The companion's appearance on the game's character-creator body (mp_male), and the
// in-game editor used to tune it. Every choice is an index, saved to KitLook.ini by label,
// so the editor and the saved file stay in step even if part lists change.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RDR2;
using RDR2.Native;

namespace KitCompanion
{
    internal class Look
    {
        // From the open-source VORP character creator (VORPCORE/vorp_character, config.lua).
        static readonly int[] BodyTypes = { 61606861, -1241887289, -369348190, 32611963, -20262001 };
        static readonly int[] Waists = { -2045421226, -1745814259, -325933489, -1065791927, -844699484, -1273449080, 927185840, 149872391, 399015098, -644349862, 1745919061, 1004225511, 1278600348, 502499352 };
        static readonly (string Name, int Hash)[] FaceFeatures =
        {
            ("Head width", 0x84D6), ("Face width", 41396), ("Forehead size", 13059),
            ("Jaw width", 0xEBAE), ("Jaw height", 0x8D0A), ("Jaw depth", 0x1DF6),
            ("Chin width", 0xC3B2), ("Chin height", 0x3C0F),
            ("Cheekbone width", 0xABCF), ("Cheekbone height", 0x6A0B),
            ("Nose width", 0x6E7F), ("Nose size", 0x3471), ("Nose height", 0x03F5), ("Nose curve", 0xF156),
            ("Ears width", 0xC04F), ("Ears angle", 0xB6CE), ("Ears height", 0x2844),
            ("Brows height", 0x3303), ("Eyes distance", 0xA54E), ("Eyes height", 0xDDFB),
            ("Mouth width", 0xF065), ("Neck width", 36277),
        };
        static readonly (string Label, (string Name, uint Hash)[] Items)[] Clothes =
        {
            ("Shirt", LookParts.Shirt), ("Vest", LookParts.Vest), ("Coat", LookParts.Coat),
            ("Pants", LookParts.Pants), ("Boots", LookParts.Boots), ("Hat", LookParts.Hat),
            ("Neckwear", LookParts.Neckwear),
        };
        static readonly (string Label, (string Name, uint Hash)[] Items)[] Tack =
        {
            ("Saddle", LookParts.HorseSaddle), ("Blanket", LookParts.HorseBlanket), ("Stirrups", LookParts.HorseStirrups),
        };

        // Parsed from LookParts: style numbers and color names, so the editor can change one without the other.
        readonly Dictionary<(int, int), uint> heads, bodiesUpper, bodiesLower;
        readonly int[] headNums, bodyNums;
        readonly List<string> hairStyles, hairColors, beardStyles, beardColors;
        readonly Dictionary<string, uint> hair, beards;

        public class Part
        {
            public string Label;
            public int Count;
            public int Value;
            public Func<int, string> Describe;
        }

        public readonly List<Part> Parts = new List<Part>();

        public Look()
        {
            heads = Grid(LookParts.Heads, @"M_HEAD_(\d+)_V_(\d+)$");
            bodiesUpper = Grid(LookParts.BodiesUpper, @"M_BODIES_UPPER_(\d+)_V_(\d+)$");
            bodiesLower = Grid(LookParts.BodiesLower, @"M_BODIES_LOWER_(\d+)_V_(\d+)$");
            headNums = heads.Keys.Select(k => k.Item1).Distinct().OrderBy(x => x).ToArray();
            bodyNums = bodiesUpper.Keys.Select(k => k.Item1).Distinct().OrderBy(x => x).ToArray();
            (hair, hairStyles, hairColors) = StyleColor(LookParts.Hair);
            (beards, beardStyles, beardColors) = StyleColor(LookParts.Beards);

            // Starting look: fair skin, tall, medium brown hair, beard a shade more ginger.
            Add("Head", headNums.Length, 0, i => "head " + headNums[i]);
            Add("Skin tone", 6, 0, i => "tone " + (i + 1) + " of 6");
            Add("Body", bodyNums.Length, 0, i => "body " + bodyNums[i]);
            Add("Body type", BodyTypes.Length, 1, i => "type " + (i + 1));
            Add("Waist", Waists.Length, 3, i => "waist " + (i + 1));
            Add("Height", 31, 15, i => (0.90 + i * 0.01).ToString("0.00") + "x (1.00 is game default, 1.05 about 6'3\")");
            Add("Hair style", hairStyles.Count, 0, i => hairStyles[i].Replace("CLOTHING_ITEM_M_", "").ToLowerInvariant());
            Add("Hair color", hairColors.Count, Math.Max(0, hairColors.IndexOf("MEDIUM_BROWN")), i => Pretty(hairColors[i]));
            Add("Beard style", beardStyles.Count + 1, 1, i => i == 0 ? "none" : beardStyles[i - 1].Replace("CLOTHING_ITEM_M_", "").ToLowerInvariant());
            Add("Beard color", beardColors.Count, Math.Max(0, beardColors.IndexOf("DARK_GINGER")), i => Pretty(beardColors[i]));
            Add("Eyes", LookParts.Eyes.Length, 0, i => "eye tint " + (i + 1));
            foreach (var f in FaceFeatures) Add(f.Name, 21, 10, i => ((i - 10) / 10.0).ToString("+0.0;-0.0;0.0"));
            foreach (var c in Clothes) Add(c.Label, c.Items.Length + 1, 0, Item(c.Items));
            foreach (var t in Tack) Add(t.Label, t.Items.Length + 1, t.Label == "Blanket" ? 0 : 1, Item(t.Items));
        }

        void Add(string label, int count, int value, Func<int, string> describe)
            => Parts.Add(new Part { Label = label, Count = Math.Max(1, count), Value = Math.Min(value, Math.Max(0, count - 1)), Describe = describe });

        int V(string label) => Parts.First(p => p.Label == label).Value;

        static Func<int, string> Item((string Name, uint Hash)[] items)
            => i => i == 0 ? "default" : items[i - 1].Name.Replace("CLOTHING_ITEM_M_", "").Replace("HORSE_EQUIPMENT_", "").ToLowerInvariant();

        static string Pretty(string color) => color.Replace('_', ' ').ToLowerInvariant();

        static Dictionary<(int, int), uint> Grid((string Name, uint Hash)[] items, string pattern)
        {
            var d = new Dictionary<(int, int), uint>();
            foreach (var it in items)
            {
                var m = Regex.Match(it.Name, pattern);
                if (m.Success) d[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = it.Hash;
            }
            return d;
        }

        static (Dictionary<string, uint>, List<string>, List<string>) StyleColor((string Name, uint Hash)[] items)
        {
            var d = new Dictionary<string, uint>();
            var styles = new List<string>();
            var colors = new List<string>();
            foreach (var it in items)
            {
                var m = Regex.Match(it.Name, @"^(.*_\d+)_([A-Z_]+)$");
                if (!m.Success) continue;
                d[m.Groups[1].Value + "|" + m.Groups[2].Value] = it.Hash;
                if (!styles.Contains(m.Groups[1].Value)) styles.Add(m.Groups[1].Value);
                if (!colors.Contains(m.Groups[2].Value)) colors.Add(m.Groups[2].Value);
            }
            styles.Sort();
            colors.Sort();
            return (d, styles, colors);
        }

        // ---------- applying ----------

        static void Wear(Ped ped, uint hash)
        {
            // The creator applies each piece twice: once plainly, once as a multiplayer item.
            PED._APPLY_SHOP_ITEM_TO_PED(ped.Handle, hash, false, false, false);
            PED._APPLY_SHOP_ITEM_TO_PED(ped.Handle, hash, false, true, false);
        }

        uint StyleColorHash(Dictionary<string, uint> map, string style, string color)
        {
            if (map.TryGetValue(style + "|" + color, out var h)) return h;
            // Not every style comes in every color: take the first color it does have.
            return map.Where(kv => kv.Key.StartsWith(style + "|")).Select(kv => kv.Value).FirstOrDefault();
        }

        public void Apply(Ped ped)
        {
            int skin = V("Skin tone") + 1;
            PED._EQUIP_META_PED_OUTFIT_PRESET(ped.Handle, 3, true);
            PED._EQUIP_META_PED_OUTFIT(ped.Handle, (uint)BodyTypes[V("Body type")]);
            PED._EQUIP_META_PED_OUTFIT(ped.Handle, (uint)Waists[V("Waist")]);

            int body = bodyNums[V("Body")];
            if (bodiesUpper.TryGetValue((body, skin), out var bu)) Wear(ped, bu);
            if (bodiesLower.TryGetValue((body, skin), out var bl)) Wear(ped, bl);
            if (heads.TryGetValue((headNums[V("Head")], skin), out var head)) Wear(ped, head);
            Wear(ped, LookParts.Eyes[V("Eyes")].Hash);

            uint hairHash = StyleColorHash(hair, hairStyles[V("Hair style")], hairColors[V("Hair color")]);
            if (hairHash != 0) Wear(ped, hairHash);
            int beard = V("Beard style");
            if (beard > 0)
            {
                uint beardHash = StyleColorHash(beards, beardStyles[beard - 1], beardColors[V("Beard color")]);
                if (beardHash != 0) Wear(ped, beardHash);
            }
            foreach (var c in Clothes)
            {
                int i = V(c.Label);
                if (i > 0) Wear(ped, c.Items[i - 1].Hash);
            }

            PED._UPDATE_PED_VARIATION(ped.Handle, false, true, true, true, false);
            Function.Call(0xAAB86462966168CE, ped.Handle, true);
            foreach (var f in FaceFeatures)
                PED._SET_PED_FACE_FEATURE(ped.Handle, f.Hash, (V(f.Name) - 10) / 10f);
            PED._UPDATE_PED_VARIATION(ped.Handle, false, true, true, true, false);
            PED._SET_PED_SCALE(ped.Handle, 0.90f + V("Height") * 0.01f);
        }

        public void ApplyHorse(Ped horse)
        {
            foreach (var t in Tack)
            {
                int i = V(t.Label);
                if (i > 0) Wear(horse, t.Items[i - 1].Hash);
            }
            PED._UPDATE_PED_VARIATION(horse.Handle, false, true, true, true, false);
        }

        // ---------- saving ----------

        public void Save(string path)
            => File.WriteAllLines(path, new[] { "; Kit's look, written by the in-game look editor." }
                .Concat(Parts.Select(p => p.Label + "=" + p.Value)));

        public void Load(string path)
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                var kv = line.Split('=');
                if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int v)) continue;
                var part = Parts.FirstOrDefault(p => p.Label == kv[0].Trim());
                if (part != null) part.Value = Math.Max(0, Math.Min(part.Count - 1, v));
            }
        }
    }
}
