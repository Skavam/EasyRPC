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
    details: "YOUR_APP_DETAILS",
    state: "YOUR_APP_STATE",
    startTimestamp: DateTime.UtcNow
);

// Keep the app running while you want the presence active.
Console.ReadKey();

await EasyRpc.ShutdownAsync();
```

Example 2 with buttons
```csharp
using EasyRPC;
using System.Collections.Generic; // REQUIRED because we're using "List<Button>". 

await EasyRpc.InitializeAsync("YOUR_DISCORD_APPLICATION_ID");

var buttons = new List<Button>
{
    // NOTE: Each RPCs can only feature two buttons, each with a label and a valid URL
    new Button { Label = "YOUR_BUTTON_NAME", Url = "YOUR_URL" },
    new Button { Label = "YOUR_BUTTON_NAME", Url = "YOUR_URL" }
};

await EasyRpc.SetPresenceAsync(
    details: "YOUR_APP_DETAILS",
    state: "YOUR_APP_STATE",
    startTimestamp: DateTime.UtcNow,
    buttons: buttons
);

// Keep the app running while you want the presence active.
Console.ReadKey();

await EasyRpc.ShutdownAsync();
```

Example 3 with a party
```csharp
using EasyRPC;

await EasyRpc.InitializeAsync("YOUR_DISCORD_APPLICATION_ID");

await EasyRpc.SetPresenceAsync(
    details: "YOUR_APP_DETAILS",
    state: "YOUR_APP_STATE",
    startTimestamp: DateTime.UtcNow,
    partyId: "YOUR_PARTY_ID",
    partySize: 5, // Amount of people currently in the party
    partyMax: 10 // Maximum of people allowed in the party
);

// Keep the app running while you want the presence active.
Console.ReadKey();

await EasyRpc.ShutdownAsync();
```
