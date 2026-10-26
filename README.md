# Year on the Record

An online music quiz for solo players or groups. Players join with a room code, listen to Spotify embeds, and guess the original release year. The host chooses an inclusive year range of at least 30 calendar years and a five-song points or nearest-wins format. Completed scores appear on a leaderboard. The catalogue currently contains 809 curated tracks, including 118 per decade from the 1960s through the 2010s and 40 from the 2020s.

## Run locally

Install the .NET 10 SDK and run:

```sh
dotnet run
```

Open the local address printed by the application. Spotify embeds require an internet connection. `songs.json` is read when the application starts; restart the app after changing the catalogue.

## Publish on Winhost

Publish a framework-dependent build with the .NET 10 SDK:

```sh
dotnet publish -c Release -o publish
```

Upload the contents of `publish` to the `/years` IIS application directory. The published `web.config` uses out-of-process ASP.NET Core hosting. The site needs the ASP.NET Core 10 hosting runtime and the `/years` directory configured as an Application Starting Point. The IIS application pool must be able to write to `/years/App_Data`.

Keep `App_Data/state.json` private and preserve it during deployments: it stores rooms, session tokens and leaderboard scores. Do not overwrite it with the publish output. To avoid file locking, take the application offline during deployment, then bring it back online. Restart the app after updating songs. The root site configuration is separate from the application's `web.config`.

## Limitations

The JSON file store and process-local lock are designed for one application process; do not run multiple workers or servers against the same file. Player names are unverified; there is no authentication, moderation, rate limiting or anti-cheat. Existing leaderboard scores combine games from different year ranges. The stated years refer to original releases and can differ from Spotify album or reissue dates. Spotify controls the availability of embeds. This is a casual game and needs further security work before an unrestricted public launch.
