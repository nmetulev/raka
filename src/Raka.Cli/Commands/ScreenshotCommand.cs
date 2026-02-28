using System.CommandLine;

namespace Raka.Cli.Commands;

internal static class ScreenshotCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string?>("element") { Description = "Element ID to screenshot (omit for whole window)" };
        var filenameOption = new Option<string?>("-f") { Description = "Save screenshot to file (default: prints base64)" };
        filenameOption.Aliases.Add("--filename");

        var command = new Command("screenshot", "Take a screenshot of the app or a specific element")
        {
            elementArg,
            filenameOption
        };

        command.SetAction(async (parseResult) =>
        {
            // Screenshot is a Phase 2 feature — stub for now
            Console.Error.WriteLine("Screenshot support is coming soon (Phase 2).");
            Console.Error.WriteLine("For now, use 'raka inspect' to see the visual tree.");
            Environment.ExitCode = 1;
            await Task.CompletedTask;
        });

        return command;
    }
}
