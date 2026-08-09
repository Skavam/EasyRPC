using EasyRPC;

try
{
    await EasyRpc.InitializeAsync("YOUR_DISCORD_APPLICATION_ID");

    await EasyRpc.SetPresenceAsync(
        details: "Using EasyRPC",
        state: "Built from scratch!",
        startTimestamp: DateTime.UtcNow
    );

    Console.ReadKey();
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    await EasyRpc.ShutdownAsync();
    Console.WriteLine("Shut down.");
}
