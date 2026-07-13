using System.Diagnostics;

namespace AbandonProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        try
        {
            return cmd switch
            {
                "probe" or "check" or "run" => Probe(args),
                "watch" => Watch(args),
                "selftest" => SelfTest(),
                _ => Unknown(cmd),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            AbandonProbe — non-freezing live dump of mission-journal Abandon UI gates

            Attaches with ReadProcessMemory only (no cdb, no inject, no freeze).

            Commands:
              probe [--process autoassault]
                  One-shot snapshot: create gate, journal tab/selection/button ptrs, diagnosis.
              watch [--process autoassault] [--interval 500]
                  Re-probe until Ctrl+C (open journal / switch tabs / select missions live).
              selftest
                  Print pure-logic create-gate table (no process).

            Workflow:
              1. Start client, log in, load a map with active missions.
              2. Open Mission Journal (Self tab), select a mission.
              3. tools\\AbandonProbe:  dotnet run --project AbandonProbe -- probe
              4. Read PRIMARY diagnosis.

            Addresses assume allowlisted autoassault build (Ghidra base 0x400000; rebased via module base).
            """);
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command '{cmd}'. Use help.");
        return 1;
    }

    private static int SelfTest()
    {
        Console.WriteLine("Create-gate truth table (helper @ 0x004CE340):");
        Console.WriteLine("fieldAc  field100  helperTrue  wouldCreate");
        foreach (var ac in new uint[] { 0, 1, 2, 3 })
        {
            foreach (var f100 in new byte[] { 0, 1 })
            {
                var g = AbandonGateAnalyzer.EvaluateCreateGate(ac, f100);
                Console.WriteLine(
                    $"{ac,7}  {f100,8}  {g.HelperReturnsTrue,10}  {g.WouldCreateAbandonButton,11}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Normal online (ac=0,100=0) → create=" +
                          AbandonGateAnalyzer.EvaluateCreateGate(0, 0).WouldCreateAbandonButton);
        return 0;
    }

    private static string ProcessName(string[] args)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] is "--process" or "-p")
                return args[i + 1];
        }

        return "autoassault";
    }

    private static int IntervalMs(string[] args)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] is "--interval" or "-i" && int.TryParse(args[i + 1], out var ms) && ms >= 100)
                return ms;
        }

        return 500;
    }

    private static int Probe(string[] args)
    {
        using var mem = ClientMemory.Attach(ProcessName(args));
        Console.WriteLine($"Attached pid={mem.Pid} base=0x{mem.ModuleBase:X8}");
        var snap = AbandonLiveProbe.Capture(mem);
        snap.WriteReport(Console.Out);
        return snap.Diagnosis.Primary == AbandonDiagnosis.ShouldBeVisible ? 0 : 0;
    }

    private static int Watch(string[] args)
    {
        var name = ProcessName(args);
        var interval = IntervalMs(args);
        Console.WriteLine($"Watching '{name}' every {interval}ms (Ctrl+C to stop)...");
        Console.WriteLine("Open journal, switch Self/Shared/Convoy, select missions.");
        Console.WriteLine();

        AbandonDiagnosis? last = null;
        uint lastAbandon = 0;
        int lastTab = int.MinValue;
        int lastSel = int.MinValue;

        while (true)
        {
            try
            {
                using var mem = ClientMemory.Attach(name);
                var snap = AbandonLiveProbe.Capture(mem);
                var changed =
                    last != snap.Diagnosis.Primary ||
                    lastAbandon != snap.AbandonBtnPtr ||
                    lastTab != snap.JournalTab ||
                    lastSel != snap.JournalSelected;

                if (changed)
                {
                    Console.WriteLine($"--- {DateTime.Now:HH:mm:ss.fff} ---");
                    Console.WriteLine(
                        $"PRIMARY={snap.Diagnosis.Primary}  create={snap.CreateGate.WouldCreateAbandonButton}  " +
                        $"abandonPtr=0x{snap.AbandonBtnPtr:X8}  tab={snap.Visibility.TabName}  " +
                        $"sel={snap.JournalSelected}  show={snap.Visibility.WouldShowAbandon}");
                    Console.WriteLine(
                        $"  gameClient +0xAC=0x{snap.GameClientFieldAc:X} +0x100=0x{snap.GameClientField100:X2}  " +
                        $"helperTrue={snap.CreateGate.HelperReturnsTrue}");
                    Console.WriteLine($"  {snap.Diagnosis.Summary}");
                    Console.WriteLine();
                    last = snap.Diagnosis.Primary;
                    lastAbandon = snap.AbandonBtnPtr;
                    lastTab = snap.JournalTab;
                    lastSel = snap.JournalSelected;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} ERROR: {ex.Message}");
            }

            Thread.Sleep(interval);
        }
    }
}
