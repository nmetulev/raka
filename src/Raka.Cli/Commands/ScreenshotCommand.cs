using System.CommandLine;

namespace Raka.Cli.Commands;

internal static class ScreenshotCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string?>("element") { Description = "Element ID to screenshot (omit for whole window)", Arity = ArgumentArity.ZeroOrOne };
        var filenameOption = new Option<string?>("-f") { Description = "Save to file instead of printing base64" };
        filenameOption.Aliases.Add("--filename");

        var command = new Command("screenshot", "Take a screenshot of the app or a specific element")
        {
            elementArg,
            filenameOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var filename = parseResult.GetValue(filenameOption);

            var parameters = new Dictionary<string, object>();
            if (element != null) parameters["element"] = element;

            using var client = await CommandHelpers.GetConnectedClient(parseResult);
            var response = await client.SendCommandAsync(Raka.Protocol.Commands.Screenshot, parameters.Count > 0 ? parameters : null);

            if (!response.Success)
            {
                Console.Error.WriteLine($"Error: {response.Error}");
                Environment.ExitCode = 1;
                return;
            }

            if (!response.Data.HasValue)
            {
                Console.Error.WriteLine("Error: No screenshot data returned");
                Environment.ExitCode = 1;
                return;
            }

            var data = response.Data.Value;
            var base64 = data.GetProperty("data").GetString()!;
            var width = data.GetProperty("width").GetInt32();
            var height = data.GetProperty("height").GetInt32();

            if (filename != null)
            {
                var bytes = Convert.FromBase64String(base64);
                await File.WriteAllBytesAsync(filename, bytes);
                Console.WriteLine($"Screenshot saved: {filename} ({width}x{height})");
            }
            else
            {
                // Print metadata as JSON (without the base64 data to keep output small)
                Console.WriteLine($"{{\"width\":{width},\"height\":{height},\"format\":\"png\",\"size\":{base64.Length * 3 / 4}}}");
                Console.WriteLine($"Use --filename to save: raka screenshot -f output.png");
            }
        });

        return command;
    }
}
