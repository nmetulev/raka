using System.CommandLine;
using System.Text.Json;

namespace Raka.Cli.Commands;

internal static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show current app status: page, theme, window size, element count");
        CommandHelpers.AddTargetOptions(command);

        command.SetAction(async (parseResult) =>
        {
            using var client = await CommandHelpers.GetConnectedClient(parseResult);
            var response = await client.SendCommandAsync(Raka.Protocol.Commands.Status);

            if (!response.Success)
            {
                Console.Error.WriteLine($"Error: {response.Error}");
                Environment.ExitCode = 1;
                return;
            }

            if (!response.Data.HasValue)
            {
                Console.Error.WriteLine("Error: No status data returned");
                Environment.ExitCode = 1;
                return;
            }

            var data = response.Data.Value;

            string Get(string prop) => data.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "—";
            int GetInt(string prop) => data.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

            Console.WriteLine($"  Title:     {Get("title")}");
            Console.WriteLine($"  Size:      {GetInt("width")}×{GetInt("height")}");
            Console.WriteLine($"  Theme:     {Get("theme")}");
            Console.WriteLine($"  Page:      {Get("currentPage")}");
            Console.WriteLine($"  Backdrop:  {Get("backdropType")}");
            Console.WriteLine($"  Elements:  {GetInt("elementCount")}");
        });

        return command;
    }
}
