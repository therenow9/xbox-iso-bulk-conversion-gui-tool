using System.Text;

// A stand-in for extract-xiso.exe so the GUI can be exercised in seconds instead of
// waiting on 4 GB images. Same command line the script uses: -x [-s] -d <dir> <iso>.
//
//   * exits 1 when the image filename starts with "fail"          -> Failed path
//   * exits 0 but writes no default.xbe when it starts with "junk" -> Failed path
//   * takes ~25s when the filename contains "slow"                 -> Cancel testing
//   * otherwise sleeps a second or two and writes default.xbe
//     plus a dummy Media\movie.wmv                                 -> OK path

Console.OutputEncoding = new UTF8Encoding(false);
Console.WriteLine("extract-xiso v2.7.1 (fake)");

string? destination = null;
string? image = null;
var skipSystemUpdate = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-x":
            break;
        case "-s":
            skipSystemUpdate = true;
            break;
        case "-d" when i + 1 < args.Length:
            destination = args[++i];
            break;
        default:
            image = args[i];
            break;
    }
}

if (destination is null || image is null)
{
    Console.Error.WriteLine("usage: fake-extract-xiso -x [-s] -d <dir> <iso>");
    return 1;
}

var name = Path.GetFileName(image);
Console.WriteLine($"opening {name}");
Console.WriteLine($"extracting to {destination}");
if (skipSystemUpdate) Console.WriteLine("skipping $SystemUpdate");

if (name.StartsWith("fail", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("reading directory table...");
    Thread.Sleep(400);
    Console.Error.WriteLine("error: unable to read sector 0x20 - image is truncated");
    Console.WriteLine("failed.");
    return 1;
}

var slow = name.Contains("slow", StringComparison.OrdinalIgnoreCase);
var steps = slow ? 50 : 8;
var files = new[]
{
    "default.xbe", "Media\\movie.wmv", "Media\\intro.wmv", "Sound\\music.xwb",
    "Data\\levels.pak", "Data\\textures.pak", "Data\\audio.pak", "Fonts\\game.xpr",
};

Directory.CreateDirectory(destination);

for (var step = 0; step < steps; step++)
{
    var file = files[step % files.Length];
    Console.WriteLine($"{file,-24} {(step + 1) * 1024 * 97:N0} bytes");
    Thread.Sleep(slow ? 500 : 150);
}

if (name.StartsWith("junk", StringComparison.OrdinalIgnoreCase))
{
    // Finishes cleanly but produces nothing usable - the script must still call this
    // a failure because no default.xbe turned up.
    Console.WriteLine("done.");
    return 0;
}

File.WriteAllBytes(Path.Combine(destination, "default.xbe"), new byte[64 * 1024]);
Directory.CreateDirectory(Path.Combine(destination, "Media"));
File.WriteAllBytes(Path.Combine(destination, "Media", "movie.wmv"), new byte[256 * 1024]);

if (!skipSystemUpdate)
{
    Directory.CreateDirectory(Path.Combine(destination, "$SystemUpdate"));
    File.WriteAllBytes(Path.Combine(destination, "$SystemUpdate", "update.xbe"), new byte[8 * 1024]);
}

Console.WriteLine("done.");
return 0;
