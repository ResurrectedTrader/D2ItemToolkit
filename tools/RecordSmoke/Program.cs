using System;
using System.Collections.Generic;
using D2ItemToolkit;

class Program
{
    static void Dump(string label, SortedDictionary<int, int> view)
    {
        Console.Write(label.PadRight(22));
        foreach (KeyValuePair<int, int> s in view)
            Console.Write("{0}{1}={2} ", ItemStatReader.StatFromKey(s.Key),
                ItemStatReader.LayerFromKey(s.Key) != 0 ? "/" + ItemStatReader.LayerFromKey(s.Key) : "", s.Value);
        Console.WriteLine();
    }

    static int Main()
    {
        // Beside the binary, so the tool works from a clone rather than one machine.
        // AppContext.BaseDirectory rather than GetDirectoryName(Assembly.Location): the latter is
        // declared nullable and is empty for a single-file publish.
        string json = System.IO.File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "sample.json"));
        Unit record = Unit.FromJson(json);

        Dump("ForSale:", ItemStatReader.ReconstructView(record, ItemStatView.ForSale()));
        Dump("Equipped:", ItemStatReader.ReconstructView(record, ItemStatView.Equipped()));
        Dump("ItemOnly:", ItemStatReader.ReconstructView(record, ItemStatView.ItemOnly()));
        Dump("SetBonus(earned):", ItemStatReader.ReconstructView(record, ItemStatView.SetBonuses(false)));
        Dump("SetBonus(all):", ItemStatReader.ReconstructView(record, ItemStatView.SetBonuses(true)));
        Dump("Everything:", ItemStatReader.ReconstructView(record, ItemStatView.Everything()));

        // A filler is a record of the same shape, so per-socket detail is just the reader again.
        int socketIndex = 0;
        foreach (IUnit socket in ItemStatReader.EnumerateSockets(record))
        {
            Dump("Socket " + socketIndex + ":",
                ItemStatReader.ReconstructView(socket, ItemStatView.ItemOnly()));
            ++socketIndex;
        }

        Console.WriteLine();
        foreach (KeyValuePair<int, uint> sock in ItemStatReader.ReadSockets(record))
            Console.WriteLine("socket {0} -> classId {1}", sock.Key, sock.Value);

        Console.WriteLine();
        foreach (ItemStatGroup g in ItemStatReader.EnumerateGroups(record))
            Console.WriteLine("state={0,-4} fromSocket={1,-5} flags=0x{2:X} stats={3}",
                g.StateNo, g.FromSocket, g.Flags, g.Stats.Count);

        return 0;
    }
}
