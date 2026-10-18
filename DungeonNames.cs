namespace MapTracker
{
    // Location prefab identity, not the generic text on a dungeon's entrance.
    internal static class DungeonNames
    {
        internal static string Get(string prefab, bool russian)
        {
            switch (prefab)
            {
                case "Crypt2": case "Crypt3": case "Crypt4": return russian ? "Погребальные камеры" : "Burial Chambers";
                case "SunkenCrypt4": return russian ? "Затонувший склеп" : "Sunken Crypt";
                case "TrollCave02": return russian ? "Пещера тролля" : "Troll Cave";
                case "MountainCave02": return russian ? "Морозные пещеры" : "Frost Caves";
                case "Mistlands_DvergrTownEntrance1": case "Mistlands_DvergrTownEntrance2": return russian ? "Заражённые шахты" : "Infested Mines";
                case "Mistlands_DvergrBossEntrance1": return russian ? "Цитадель Королевы" : "Queen's Citadel";
                case "MorgenHole1": case "MorgenHole2": case "MorgenHole3": return russian ? "Пещера моргена" : "Morgen Cave";
                case "Hildir_cave": return russian ? "Воющая пещера" : "Howling Cavern";
                case "Hildir_crypt": return russian ? "Тлеющая гробница" : "Smouldering Tomb";
                case "Hildir_plainsfortress": return russian ? "Запечатанная башня" : "Sealed Tower";
                case "CharredFortress": return russian ? "Обугленная крепость" : "Charred Fortress";
                default: return null;
            }
        }
    }
}
