using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class ScreenshotCommand
{
    public static Command Create()
    {
        var elementArg = new Argument<string?>("element") { Description = "Element ID to screenshot (omit for whole window)", Arity = ArgumentArity.ZeroOrOne };
        var filenameOption = new Option<string?>("-f") { Description = "Save to file instead of printing base64" };
        filenameOption.Aliases.Add("--filename");
        var modeOption = new Option<string?>("--mode") { Description = "Screenshot mode: 'capture' (pixel-perfect, includes Mica/Acrylic) or 'render' (RenderTargetBitmap, works offscreen). Default: capture for window, render for element." };
        var bgOption = new Option<string?>("--bg") { Description = "Background color for render mode (e.g. '#FFFFFF'). Composites element onto solid color to fix invisible text." };
        var stateOption = new Option<string?>("--state") { Description = "Temporarily apply visual state before capture, then revert (e.g., PointerOver, Pressed)" };
        stateOption.Aliases.Add("-s");

        var command = new Command("screenshot", "Take a screenshot of the app or a specific element")
        {
            elementArg,
            filenameOption,
            modeOption,
            bgOption,
            stateOption
        };
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            var element = parseResult.GetValue(elementArg);
            var filename = parseResult.GetValue(filenameOption);
            var mode = parseResult.GetValue(modeOption);
            var bg = parseResult.GetValue(bgOption);
            var state = parseResult.GetValue(stateOption);

            var parameters = new ScreenshotParams(element, mode, bg, state);
            var paramsJson = JsonSerializer.SerializeToElement(parameters, CliJsonContext.Default.ScreenshotParams);

            using var client = await CommandHelpers.GetConnectedClient(parseResult);
            var response = await client.SendCommandAsync(Raka.Protocol.Commands.Screenshot, paramsJson);

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
            var usedMode = data.GetProperty("mode").GetString() ?? "unknown";

            if (filename != null)
            {
                var bytes = Convert.FromBase64String(base64);
                await File.WriteAllBytesAsync(filename, bytes);
                Console.WriteLine($"Screenshot saved: {filename} ({width}x{height}, mode={usedMode})");
            }
            else
            {
                Console.WriteLine($"{{\"width\":{width},\"height\":{height},\"format\":\"png\",\"mode\":\"{usedMode}\",\"size\":{base64.Length * 3 / 4}}}");
                Console.WriteLine($"Use -f to save: raka screenshot -f output.png");
                Console.WriteLine($"Modes: --mode capture (pixel-perfect) | --mode render (offscreen, use --bg '#FFF' for background)");
            }

            // Show hint if backdrop was auto-detected
            if (data.TryGetProperty("hint", out var hintProp) && hintProp.ValueKind == JsonValueKind.String)
            {
                Console.Error.WriteLine($"💡 {hintProp.GetString()}");
            }
        });

        return command;
    }
}
