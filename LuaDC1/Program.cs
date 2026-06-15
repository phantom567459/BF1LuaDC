using LuaDC1;
using LuaDC1.Gui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--ui" or "--gui")
            return MainForm.Run(args[1..]);
        return Cli.Run(args);
    }
}
