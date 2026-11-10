using OpenSWR.DrawDiff;

const string Usage = """
    usage:
      drawdiff <original.jsonl> <openswr.jsonl> [--window t0..t1] [--alias res=res]... [--out report.txt]
      drawdiff --make-vector <in.jsonl> <out.jsonl> [--window t0..t1]
    --window: original records from t0 to t1 ms after its first record (make-vector: of the input)
    --alias:  treat an openswr res id as an original one, e.g. strategy.dll:2:900=strategy.dll:2:901 (other side's skin)
    """;

var files = new List<string>();
var alias = new Dictionary<string, string>();
(long, long)? window = null;
string? output = null;
bool makeVector = false;
try
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--window": window = Vector.ParseWindow(args[++i]); break;
            case "--out": output = args[++i]; break;
            case "--make-vector": makeVector = true; break;
            case "--alias":
                var kv = args[++i].Split('=');
                if (kv.Length != 2) throw new FormatException($"alias '{args[i]}': expected res=res");
                alias[kv[0]] = kv[1];
                break;
            default: files.Add(args[i]); break;
        }
    }
    if (files.Count != 2) throw new FormatException("two files expected");
}
catch (Exception e) when (e is FormatException or IndexOutOfRangeException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(Usage);
    return 2;
}

if (makeVector)
{
    File.WriteAllLines(files[1], Vector.Make(File.ReadLines(files[0]), window));
    return 0;
}

var report = Differ.Compare(TraceReader.Read(File.ReadLines(files[0]), window), OpenSWRLogReader.Read(File.ReadLines(files[1]), alias));
string text = report.Text(Path.GetFileName(files[0]), Path.GetFileName(files[1]));
if (output is null) Console.Write(text);
else { File.WriteAllText(output, text); Console.WriteLine(report.Summary); }
return 0;
