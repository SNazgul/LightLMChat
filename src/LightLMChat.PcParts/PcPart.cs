namespace LightLMChat.PcParts;

/// <summary>A single catalog entry describing an IBM-PC-compatible part from the 286–486 era.</summary>
public sealed record PcPart(
    string Id,
    string Type,
    string Subtype,
    string Company,
    string Characteristics,
    string Description,
    int PriceUsd);

/// <summary>In-memory, read-only catalog of IBM PC parts (x286–x486 era).</summary>
public sealed class PcPartsCatalog
{
    private readonly IReadOnlyList<PcPart> _parts = BuildCatalog();

    public IReadOnlyList<PcPart> All => _parts;

    public IReadOnlyList<string> Types => _parts
        .Select(p => p.Type)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(t => t, StringComparer.Ordinal)
        .ToArray();

    public PcPart? GetById(string id) => _parts.FirstOrDefault(p => p.Id == id);

    /// <summary>The lowest-priced part of the given type, or null if the type is unknown.</summary>
    public PcPart? Cheapest(string type) => _parts
        .Where(p => p.Type == type)
        .OrderBy(p => p.PriceUsd)
        .ThenBy(p => p.Subtype, StringComparer.Ordinal)
        .FirstOrDefault();

    public IReadOnlyList<PcPart> Search(string query, string? partType = null)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToArray();
        if (terms.Length == 0) return [];
        var namedCompany = _parts.Select(p => p.Company)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(company => terms.Contains(company.ToLowerInvariant()));

        return _parts
            .Where(p => partType is null || p.Type.Equals(partType, StringComparison.OrdinalIgnoreCase))
            .Where(p => namedCompany is null || p.Company.Equals(namedCompany, StringComparison.OrdinalIgnoreCase))
            .Select(p => new { Part = p, Score = Score(p, terms) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Part.Subtype, StringComparer.Ordinal)
            .Take(8)
            .Select(x => x.Part)
            .ToArray();
    }

    private static int Score(PcPart part, string[] terms)
    {
        // Earlier fields carry more weight: subtype, company, type, then characteristics/description.
        var fields = new[] { part.Subtype, part.Company, part.Type, part.Characteristics, part.Description };
        var score = 0;
        foreach (var term in terms)
        {
            var matched = false;
            for (var i = 0; i < fields.Length; i++)
            {
                if (fields[i].Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += fields.Length - i;
                    matched = true;
                    break;
                }
            }
            if (!matched) return 0;
        }
        return score;
    }

    private static IReadOnlyList<PcPart> BuildCatalog() =>
    [
        // ---- CPU ----
        new("cpu-286-12", "CPU", "Intel 80286 (286)", "Intel", "12 MHz, 16-bit, PLCC",
            "The 16-bit workhorse of the late-1980s AT-class PC. Runs DOS and early Windows with real multitasking support over the original 8088.", 180),
        new("cpu-386sx-16", "CPU", "Intel 80386SX (386SX)", "Intel", "16 MHz, 32-bit core, 16-bit bus",
            "A budget 386 that brings 32-bit software support to cheaper 16-bit motherboards, so you can run Windows 3.1 and 32-bit DOS apps.", 220),
        new("cpu-386dx-33", "CPU", "Intel 80386DX (386DX)", "Intel", "33 MHz, 32-bit, 4 GB address space",
            "The full 32-bit 386 — the baseline for Windows 3.x, OS/2, and 32-bit protected-mode DOS games.", 320),
        new("cpu-486sx-25", "CPU", "Intel 80486SX (486SX)", "Intel", "25 MHz, no built-in FPU",
            "A 486 without the built-in math coprocessor. Cheaper for word processing; add a 487 coprocessor later if you need floating point.", 350),
        new("cpu-486dx-33", "CPU", "Intel 80486DX (486DX)", "Intel", "33 MHz, built-in FPU + 8 KB cache",
            "The standard 486. Its on-chip cache and math coprocessor make it much faster per MHz than the 386 for Windows and spreadsheets.", 520),
        new("cpu-486dx2-66", "CPU", "Intel 80486DX2 (486DX2)", "Intel", "66 MHz core on a 33 MHz bus, clock-doubled",
            "A clock-doubled 486 running the core at 66 MHz on a 33 MHz board — an easy speed upgrade for a 486DX-33 system.", 650),
        new("cpu-486dx4-100", "CPU", "Intel 80486DX4 (486DX4)", "Intel", "100 MHz, 3.3 V, 16 KB cache",
            "A fast late-era 486 with a larger cache and 3.3 V operation. Ideal for a quick, quiet Windows 3.1 workstation.", 850),
        new("cpu-am386dx-40", "CPU", "AMD Am386DX-40", "AMD", "40 MHz, 32-bit",
            "A licensed 386 clone running at 40 MHz — a low-cost route to near-486 DOS performance.", 160),
        new("cpu-am486dx2-66", "CPU", "AMD Am486DX2-66", "AMD", "66 MHz, clock-doubled 486",
            "A compatible 486DX2 that often costs less than the Intel part and fits the same socket.", 480),
        new("cpu-cx486dlc-33", "CPU", "Cyrix Cx486DLC-33", "Cyrix", "33 MHz, 486-class, 386 socket",
            "A 486-class CPU for 386 motherboards — a cheap upgrade path, though some software needs a patch for full compatibility.", 150),

        // ---- Motherboard ----
        new("mb-286-at", "Motherboard", "286 Baby AT (ISA)", "Generic", "ISA slots, 1 MB base RAM, keyboard controller",
            "The board that ties every component together. This Baby AT board is the base for a compact 286 system with ISA cards.", 90),
        new("mb-386-isa", "Motherboard", "386 Baby AT (ISA)", "Generic", "386 socket, 4×ISA, 8×30-pin SIMM, cache socket",
            "A 386 motherboard with ISA slots, SIMM sockets, and an optional external cache. A good base for a mid-range 386 PC.", 130),
        new("mb-486-vlb", "Motherboard", "486 Baby AT (ISA + VLB)", "Generic", "486 socket, 3×ISA, 2×VLB, 72-pin SIMM",
            "A 486 board with VESA Local Bus slots so video and disk controllers talk directly to the CPU — much faster graphics and disk I/O.", 180),
        new("mb-486-pci", "Motherboard", "486 PCI/ISA", "Generic", "486 socket, PCI + ISA slots, EDO RAM support",
            "A late 486 board with PCI slots for modern graphics and network cards, while keeping ISA for older sound and modem cards.", 200),

        // ---- Memory ----
        new("ram-simm30-1m", "Memory", "1 MB SIMM 30-pin (FPM)", "Generic", "30-pin, 70–80 ns, fast page mode",
            "Early 386/486 memory modules, installed in banks of four to reach the memory your motherboard supports.", 45),
        new("ram-simm30-4m", "Memory", "4 MB SIMM 30-pin", "Generic", "30-pin, 70 ns",
            "Higher-density 30-pin SIMMs for 386 systems that support them, reaching 8 MB or more without filling every socket.", 160),
        new("ram-simm72-4m", "Memory", "4 MB SIMM 72-pin (FPM)", "Generic", "72-pin, 70 ns, fast page mode",
            "The standard memory module for 486 systems, installed one at a time on the 32-bit data path.", 130),
        new("ram-simm72-8m", "Memory", "8 MB SIMM 72-pin (EDO)", "Generic", "72-pin, 60 ns, extended data out",
            "Extended Data Out memory, slightly faster than FPM and well suited to fast 486 and early Pentium boards.", 260),
        new("ram-dip-256k", "Memory", "256 KB DIP RAM", "Generic", "DIP chips, for 286 boards",
            "Individual DIP memory chips for older 286 motherboards that predate SIMM sockets.", 20),

        // ---- Hard drive ----
        new("hdd-st225-20", "Hard drive", "Seagate ST-225 (20 MB MFM)", "Seagate", "20 MB, MFM, 5.25\" half-height",
            "A classic 1980s MFM hard drive. Paired with an MFM controller card it gives a 286 a modest but fast local disk for DOS.", 300),
        new("hdd-wd340-ide", "Hard drive", "Western Digital Caviar 340 MB IDE", "Western Digital", "340 MB, IDE, 3.5\"",
            "A roomy IDE drive for a 386/486. IDE plugs straight into many motherboards or an inexpensive controller card.", 250),
        new("hdd-conner120", "Hard drive", "Conner Peripherals 120 MB IDE", "Conner", "120 MB, IDE, 3.5\"",
            "A reliable mid-size IDE drive with plenty of space for DOS, Windows 3.1, and applications.", 180),
        new("hdd-maxtor540", "Hard drive", "Maxtor 540 MB IDE", "Maxtor", "540 MB, IDE, 3.5\"",
            "A large IDE drive for a 486 — enough for Windows plus a big set of games and utilities.", 360),

        // ---- Floppy drive ----
        new("fdd-35-144", "Floppy drive", "3.5\" 1.44 MB floppy", "Generic", "3.5\", 1.44 MB, high density",
            "The standard 1990s floppy drive — install software, back up small files, and boot the system.", 60),
        new("fdd-525-12", "Floppy drive", "5.25\" 1.2 MB floppy", "Generic", "5.25\", 1.2 MB, high density",
            "A 5.25\" drive for reading older software and archives from the 1980s.", 85),

        // ---- Display ----
        new("disp-vga-mono", "Display", "12\" monochrome VGA monitor", "Generic", "12\", VGA, monochrome (amber/white)",
            "A crisp text monitor for word processing and business use — cheaper than a color VGA.", 120),
        new("disp-vga-color", "Display", "14\" VGA color monitor", "Generic", "14\", VGA 640×480, 0.31 mm",
            "A basic color monitor for DOS text and early VGA graphics — the everyday choice for a 386/486.", 300),
        new("disp-svga-14", "Display", "14\" SVGA color monitor", "Generic", "14\", up to 1024×768, 0.28 mm",
            "A sharper SVGA monitor for Windows and CAD; pairs with an SVGA card for higher resolutions.", 420),

        // ---- Video card ----
        new("vga-ibm", "Video card", "IBM VGA (256 KB)", "IBM", "VGA 640×480, 256 KB",
            "The standard VGA adapter — crisp 640×480 color and the required baseline for DOS and Windows graphics.", 110),
        new("vga-trident8900", "Video card", "Trident TVGA 8900", "Trident", "SVGA, 1 MB, ISA",
            "A popular SVGA card with 1 MB for 256 colors at 800×600 — good for Windows and games.", 160),
        new("vga-et4000", "Video card", "Tseng Labs ET4000", "Tseng Labs", "SVGA, ISA/VLB, very fast",
            "A fast SVGA chipset loved by gamers; VLB versions are among the fastest DOS video cards.", 210),
        new("vga-cl5428", "Video card", "Cirrus Logic CL-GD5428", "Cirrus Logic", "SVGA, VESA local bus, 1 MB",
            "A VLB SVGA card with strong Windows acceleration — a great match for a 486 VLB motherboard.", 190),

        // ---- Mouse ----
        new("mouse-ms-serial", "Mouse", "Microsoft serial mouse (2-button)", "Microsoft", "Serial port, 2 buttons",
            "A standard serial mouse for DOS and Windows pointing; connects to the 9-pin serial port.", 40),
        new("mouse-logi-serial", "Mouse", "Logitech serial mouse (3-button)", "Logitech", "Serial port, 3 buttons",
            "A reliable serial mouse with an extra middle button for CAD and utilities.", 55),
        new("mouse-ps2", "Mouse", "PS/2 mouse", "Generic", "PS/2 port, 2–3 buttons",
            "A PS/2 mouse that plugs into the dedicated PS/2 port, freeing a serial port for a modem.", 40),

        // ---- Keyboard ----
        new("kb-101", "Keyboard", "101-key AT keyboard", "Generic", "101 keys, AT connector",
            "The standard 101-key keyboard with function keys and a separate cursor pad — comfortable DOS and Windows use.", 45),
        new("kb-84", "Keyboard", "84-key AT keyboard", "Generic", "84 keys, AT connector",
            "The original AT keyboard with 84 keys; compact and suitable for early 286/386 systems.", 30),

        // ---- Cooling ----
        new("cool-486", "Cooling", "486 CPU cooler (heatsink + fan)", "Generic", "40 mm fan, fits 486 sockets",
            "Keeps a 486 — especially a DX2/DX4 — cool and stable. Essential for fast clock-doubled parts.", 15),
        new("cool-case-fan", "Cooling", "80 mm case fan", "Generic", "80 mm, 12 V",
            "Improves airflow through the case to keep drives and cards cool.", 8),
        new("cool-386-hs", "Cooling", "386 passive heatsink", "Generic", "Clip-on, no fan",
            "A passive heatsink for a 386 CPU, which runs cool enough not to need a fan.", 5),

        // ---- Power supply ----
        new("psu-200-at", "Power supply", "200 W AT power supply", "Generic", "200 W, AT, fan",
            "Powers the motherboard and drives. 200 W is enough for a 386 or a modest 486 with one hard drive.", 55),
        new("psu-250-at", "Power supply", "250 W AT power supply", "Generic", "250 W, AT, fan",
            "A larger AT supply for a fully loaded 486 with multiple drives and cards.", 75),

        // ---- Case ----
        new("case-mini-tower", "Case", "Mini tower AT case", "Generic", "Baby AT, 2×5.25\", 2×3.5\" bays",
            "A compact tower with room for a Baby AT board, two large and two small drives.", 70),
        new("case-desktop", "Case", "Desktop AT case", "Generic", "Baby AT, flat desktop form",
            "A flat desktop case that the monitor can sit on — classic 1980s styling.", 60),

        // ---- Sound card ----
        new("sound-sb16", "Sound card", "Sound Blaster 16", "Creative Labs", "16-bit, FM + digital audio, ISA",
            "The de-facto standard sound card for games and multimedia: 16-bit digital audio with Sound Blaster/AdLib compatibility.", 160),
        new("sound-sbpro", "Sound card", "Sound Blaster Pro", "Creative Labs", "8-bit, stereo, ISA",
            "A stereo 8-bit Sound Blaster for DOS games — a step up from the original mono card.", 110),

        // ---- I/O controller / card ----
        new("io-multio-vlb", "I/O controller", "VLB multi-I/O (IDE + floppy + ports)", "Generic", "VLB, 2×IDE, floppy, 2×serial, 1×parallel",
            "Combines IDE, floppy, serial, and parallel ports on one VESA Local Bus card — frees ISA slots and speeds disk I/O.", 65),
        new("io-serial-par", "I/O card", "Serial/parallel I/O card", "Generic", "2×serial, 1×parallel, ISA",
            "Adds serial ports for a mouse and modem and a parallel port for a printer.", 30),
    ];
}
