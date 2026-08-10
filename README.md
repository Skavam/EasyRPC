# EasyRPC
EasyRPC is a dead-simple, zero-dependency Discord Rich Presence library for .NET 8

## Features

- **One‑line setup** – just call `InitializeAsync()` with your Discord Application ID.
- **Self‑contained** – no external NuGet packages; pure .NET 8.
- **Auto‑reconnect** – restores the presence when Discord restarts.
- **Fully async** – non‑blocking operations.
- **Rich Presence support** – buttons, timestamps, party info, images.

## Getting Started

### 1. Install / Reference

Clone the repository or download the source. Reference the `EasyRPC` project in your solution.

### 2. In your code 

Example 1 with no buttons
```csharp
using EasyRPC;

await EasyRpc.InitializeAsync("YOUR_DISCORD_APPLICATION_ID");

await EasyRpc.SetPresenceAsync(
    details: "Playing my game",
    state: "In the menu",
    startTimestamp: DateTime.UtcNow
);

// Keep the app running while you want the presence active.
Console.ReadKey();

await EasyRpc.ShutdownAsync();
```

Example 2 with buttons
```csharp
using EasyRPC;
using System.Collections.Generic; 

await EasyRpc.InitializeAsync("YOUR_DISCORD_APPLICATION_ID");

var buttons = new List<Button>
{
    new Button { Label = "Test1", Url = "https://www.google.com/" },
    new Button { Label = "Test2", Url = "https://www.google.com/" }
};

await EasyRpc.SetPresenceAsync(
    details: "Playing my game",
    state: "In the menu",
    startTimestamp: DateTime.UtcNow,
    buttons: buttons
);

// Keep the app running while you want the presence active.
Console.ReadKey();

await EasyRpc.ShutdownAsync();
```
