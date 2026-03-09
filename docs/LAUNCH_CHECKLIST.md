# Launch Checklist

- Verify Windows, Linux, and macOS export presets.
- Run `dotnet build VoxelSiege.sln` and `dotnet test VoxelSiege.sln` in CI.
- Confirm Steam `steam_appid.txt` is replaced with the production app ID before shipping builds.
- Smoke test lobby flow, local sandbox, bot match, and one networked match.
- Validate profile, replay, and settings saves.
- Capture launch screenshots, trailer footage, and store assets.
