using System;
using System.Collections.Generic;

namespace MapTracker
{
    // Capability replies arrive independently. Keep each player's UI object alive
    // while its state changes so another reply cannot reset focus or hover.
    internal sealed class PresetRecipientRows<TRow> where TRow : class
    {
        private readonly Dictionary<long, TRow> rows = new Dictionary<long, TRow>();
        private readonly HashSet<long> present = new HashSet<long>();
        private readonly List<long> removed = new List<long>();

        internal void Refresh(IList<PresetSharing.Recipient> players,
            Func<PresetSharing.Recipient, TRow> create,
            Action<TRow, PresetSharing.Recipient, int> update, Action<TRow> remove)
        {
            present.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                present.Add(player.Id);
                if (!rows.TryGetValue(player.Id, out var row))
                    rows.Add(player.Id, row = create(player));
                update(row, player, i);
            }
            removed.Clear();
            foreach (var pair in rows)
                if (!present.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (long id in removed)
            {
                var row = rows[id];
                rows.Remove(id);
                remove(row);
            }
        }

        // The owning dialog deactivates/destroys the entire hierarchy on close.
        internal void Clear() { rows.Clear(); present.Clear(); removed.Clear(); }
    }
}
