namespace MapTracker
{
    internal static class DungeonProgress
    {
        // A logical revision follows the observed state, independent of PC clocks.
        // The author resolves concurrent clicks deterministically on both clients.
        internal static void Toggle(MarkerRecord record, long author)
        {
            record.completionRevision = checked(System.Math.Max(0, record.completionRevision) + 1);
            record.completionAuthor = author;
            record.completed = !record.completed;
            record.nameCache = null;
        }
        internal static bool Merge(MarkerRecord record, MarkerRecord incoming)
        {
            bool wasCompleted = record.completed, wasVisited = record.visited;
            long revision = record.completionRevision, author = record.completionAuthor;
            record.visited |= incoming.visited;
            if (incoming.completionRevision > 0 && (incoming.completionRevision > revision ||
                incoming.completionRevision == revision && incoming.completionAuthor > author))
            {
                record.completed = incoming.completed;
                record.completionRevision = incoming.completionRevision;
                record.completionAuthor = incoming.completionAuthor;
            }
            else if (revision == 0 && incoming.completionRevision == 0)
                record.completed |= incoming.completed; // Preserve legacy positive progress.
            bool changed = wasCompleted != record.completed || wasVisited != record.visited ||
                revision != record.completionRevision || author != record.completionAuthor;
            if (changed) record.nameCache = null;
            return changed;
        }
    }
}
