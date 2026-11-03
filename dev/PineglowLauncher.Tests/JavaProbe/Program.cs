// Real subprocess fixture: behaviour is selected by a file next to its copied apphost.
string mode = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mode.txt")).Trim();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pid.txt"), Environment.ProcessId.ToString());
if (mode == "slow") await Task.Delay(TimeSpan.FromSeconds(15));
if (mode == "flood")
{
    // Larger than either redirected pipe buffer; sequential reads would deadlock.
    Console.Out.Write(new string('x', 256 * 1024));
}
Console.Error.WriteLine(mode == "invalid" ? "not a Java runtime" : "openjdk version \"21.0.8\"");
return mode == "failure" ? 1 : 0;
