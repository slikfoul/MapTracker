using MapTracker;

int checks = 0;
void Check(bool pass, string name)
{
    if (!pass) throw new Exception(name);
    checks++; Console.WriteLine("PASS " + name);
}
PresetSharing.Recipient Player(long id, string name, PresetSharing.Availability state) =>
    new() { Id = id, Name = name, State = state };

var list = new PresetRecipientRows<Row>();
var visible = new Dictionary<long, Row>();
int created = 0, removed = 0;
void Refresh(params PresetSharing.Recipient[] players) => list.Refresh(players,
    player => { created++; var row = new Row(); visible.Add(player.Id, row); return row; },
    (row, player, index) => { row.Recipient = player; row.Index = index; },
    row => { removed++; visible.Remove(row.Recipient.Id); });

Refresh(Player(1, "Alice", PresetSharing.Availability.Checking), Player(2, "Bob", PresetSharing.Availability.Checking));
var alice = visible[1]; var bob = visible[2];
Check(created == 2 && removed == 0, "initial online roster creates one row per peer");
Refresh(Player(1, "Alice", PresetSharing.Availability.Ready), Player(2, "Bob", PresetSharing.Availability.Checking));
Check(ReferenceEquals(visible[1], alice) && ReferenceEquals(visible[2], bob), "first capability reply preserves both UI objects");
Check(alice.Recipient.State == PresetSharing.Availability.Ready && bob.Recipient.State == PresetSharing.Availability.Checking,
    "independent capability reply updates only its recipient state");
Refresh(Player(1, "Alice", PresetSharing.Availability.Ready), Player(2, "Bob", PresetSharing.Availability.NotAccepting));
Check(ReferenceEquals(visible[1], alice) && ReferenceEquals(visible[2], bob) && created == 2 && removed == 0,
    "later reply does not recreate the already-ready or hovered row");
Check(bob.Recipient.State == PresetSharing.Availability.NotAccepting, "blocked recipient state reaches retained row");
for (int i = 0; i < 100; i++)
    Refresh(Player(1, "Alice", PresetSharing.Availability.Ready), Player(2, "Bob", PresetSharing.Availability.NotAccepting));
Check(created == 2 && removed == 0 && ReferenceEquals(visible[1], alice), "unchanged polling never recreates UI objects");

Refresh(Player(2, "Aaron", PresetSharing.Availability.Ready), Player(1, "Alice", PresetSharing.Availability.Ready));
Check(ReferenceEquals(visible[2], bob) && bob.Index == 0 && alice.Index == 1, "rename and sorted reorder retain peer identity");
Check(bob.Recipient.Name == "Aaron" && bob.Recipient.State == PresetSharing.Availability.Ready,
    "retained row receives current name and capability for click and hint callbacks");
Refresh(Player(2, "Aaron", PresetSharing.Availability.Unavailable), Player(1, "Alice", PresetSharing.Availability.Ready));
Check(ReferenceEquals(visible[2], bob) && bob.Recipient.State == PresetSharing.Availability.Unavailable,
    "actual timeout remains visible without recreating rows");

Refresh(Player(3, "Aaron", PresetSharing.Availability.Checking), Player(2, "Aaron", PresetSharing.Availability.Ready),
    Player(1, "Alice", PresetSharing.Availability.Ready));
var third = visible[3];
Check(created == 3 && removed == 0 && !ReferenceEquals(third, bob), "same names use separate stable player IDs");
Check(bob.Index == 1 && alice.Index == 2, "join updates ordering without replacing existing rows");
Refresh(Player(3, "Aaron", PresetSharing.Availability.Ready), Player(1, "Alice", PresetSharing.Availability.Ready));
Check(removed == 1 && !visible.ContainsKey(2) && ReferenceEquals(visible[1], alice) && ReferenceEquals(visible[3], third),
    "departure retires exactly the departed row");
Refresh(Player(3, "Aaron", PresetSharing.Availability.Ready), Player(1, "Alice", PresetSharing.Availability.Ready),
    Player(2, "Zed", PresetSharing.Availability.Checking));
Check(created == 4 && !ReferenceEquals(visible[2], bob) && visible[2].Recipient.State == PresetSharing.Availability.Checking,
    "rejoining player gets a fresh row without stale ready state");
Refresh();
Check(removed == 4 && visible.Count == 0, "empty roster retires each remaining row once");
Refresh();
Check(removed == 4, "repeated empty roster does not repeat retirement");

Refresh(Player(1, "Alice", PresetSharing.Availability.Ready));
var beforeClose = visible[1];
list.Clear(); visible.Clear(); // The dialog owns destruction of its whole hierarchy.
Refresh(Player(1, "Alice", PresetSharing.Availability.Ready));
Check(!ReferenceEquals(beforeClose, visible[1]), "closed dialog cannot reuse a destroyed row when reopened");
Check(removed == 4, "clearing a destroyed dialog does not invoke stale row callbacks");
Console.WriteLine($"{checks} checks passed.");

internal sealed class Row
{
    internal PresetSharing.Recipient Recipient;
    internal int Index;
}

namespace MapTracker
{
    // These are the protocol data fields consumed by the real UI reconciler.
    internal static class PresetSharing
    {
        internal enum Availability { Checking, Ready, Unavailable, NotAccepting }
        internal sealed class Recipient
        {
            internal long Id;
            internal string Name;
            internal Availability State;
        }
    }
}
