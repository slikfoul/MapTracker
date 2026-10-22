using System;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    internal static class CreatureIconResolver
    {
        internal static Sprite Resolve(GameObject creature, string prefab)
        {
            // The buildable dummy is also a Character, so the creature branch must
            // explicitly retain its native construction-menu icon.
            if (prefab == "piece_TrainingDummy")
                return (creature ?? ZNetScene.instance?.GetPrefab(prefab))?.GetComponent<Piece>()?.m_icon;
            string explicitItem = ExplicitItemPrefab(prefab);
            if (explicitItem != null) return TrophyItem(explicitItem);
            string adult;
            switch (prefab)
            {
                case "Greyling": return TrophyItem("TrophyGreyling") ?? Trophy(creature) ??
                    TrophyItem("TrophyGreydwarf") ?? Trophy(ZNetScene.instance?.GetPrefab("Greydwarf"));
                case "Boar_piggy": adult = "Boar"; break;
                case "Wolf_cub": adult = "Wolf"; break;
                case "Asksvin_hatchling": adult = "Asksvin"; break;
                case "Lox_Calf": adult = "Lox"; break;
                default: return Trophy(creature) ?? TrophyItem("Trophy" + prefab) ?? CustomCreatureIcons.For(prefab) ??
                    TrophyItem(RelatedItemPrefab(prefab));
            }
            return CustomCreatureIcons.For(prefab) ?? Trophy(ZNetScene.instance?.GetPrefab(adult)) ??
                TrophyItem("Trophy" + adult) ??
                Trophy(creature);
        }
        // Twitchers have no trophy; use the native Charred Skull item portrait for both forms.
        internal static string ExplicitItemPrefab(string prefab)
            => prefab == "Charred_Twitcher" || prefab == "Charred_Twitcher_Summoned" ? "Charredskull" : null;
        // Only exact known variants inherit a relative's portrait. Own trophies and
        // custom portraits above keep priority; unrelated/modded names remain untouched.
        internal static string RelatedItemPrefab(string prefab)
        {
            switch (prefab)
            {
                case "Aspect_Bonemass": return "TrophyBonemass";
                case "Aspect_Eikthyr": return "TrophyEikthyr";
                case "Aspect_Elder": return "TrophyTheElder";
                case "Aspect_Fader": return "TrophyFader";
                case "Aspect_Moder": return "TrophyDragonQueen";
                case "Aspect_SeekerQueen": return "TrophySeekerQueen";
                case "Aspect_Yagluth": return "TrophyGoblinKing";
                case "Bjorn_spiritcaller": return "TrophyBjorn";
                case "Boar_spiritcaller": return "TrophyBoar";
                case "Moose_calf": case "Moose_spiritcaller": return "TrophyMoose";
                case "Wolf_spiritcaller": return "TrophyWolf";
                case "BlobMorkMini": return "TrophyBlob_Morkhalla";
                case "Charred_Archer_Fader": return "TrophyCharredArcher";
                case "Charred_Melee_Dyrnwyn": case "Charred_Melee_Fader": return "TrophyCharredMelee";
                case "Ghost_old": case "Ghost_Void": return "TrophyGhost";
                case "Goblin_Gem": case "GoblinDeepNorth": return "TrophyGoblin";
                case "GoblinBrute_Hildir": return "TrophyGoblinBruteBrosBrute";
                case "Seal_Pup": return "TrophySeal";
                case "SeekerBrood": return "TrophySeeker";
                case "Skeleton_Friendly": return "TrophySkeleton";
                case "Troll_Summoned": case "TrollFrost": return "TrophyFrostTroll";
                default: return null;
            }
        }
        private static Sprite TrophyItem(string prefab)
            => string.IsNullOrEmpty(prefab) ? null : (ObjectDB.instance?.GetItemPrefab(prefab) ?? ZNetScene.instance?.GetPrefab(prefab))?
                .GetComponent<ItemDrop>()?.m_itemData.GetIcon();
        private static Sprite Trophy(GameObject creature)
        {
            var drops = creature != null ? creature.GetComponent<CharacterDrop>() : null;
            if (drops == null) return null;
            foreach (var drop in drops.m_drops.Where(d => d.m_prefab != null &&
                d.m_prefab.name.StartsWith("Trophy", StringComparison.OrdinalIgnoreCase)))
            {
                var icon = drop.m_prefab.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
                if (icon != null) return icon;
            }
            return null;
        }
    }
}
