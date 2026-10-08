using System;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    internal static class CreatureIconResolver
    {
        internal static Sprite Resolve(GameObject creature, string prefab)
        {
            string adult;
            switch (prefab)
            {
                case "Greyling": return TrophyItem("TrophyGreyling") ?? Trophy(creature) ??
                    TrophyItem("TrophyGreydwarf") ?? Trophy(ZNetScene.instance?.GetPrefab("Greydwarf"));
                case "Boar_piggy": adult = "Boar"; break;
                case "Wolf_cub": adult = "Wolf"; break;
                case "Asksvin_hatchling": adult = "Asksvin"; break;
                case "Lox_Calf": adult = "Lox"; break;
                default: return Trophy(creature) ?? TrophyItem("Trophy" + prefab) ?? CustomCreatureIcons.For(prefab);
            }
            return CustomCreatureIcons.For(prefab) ?? Trophy(ZNetScene.instance?.GetPrefab(adult)) ??
                TrophyItem("Trophy" + adult) ??
                Trophy(creature);
        }
        private static Sprite TrophyItem(string prefab)
            => (ObjectDB.instance?.GetItemPrefab(prefab) ?? ZNetScene.instance?.GetPrefab(prefab))?
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
