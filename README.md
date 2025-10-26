# Twitch Bot

[![CI](https://github.com/viral32111/twitch-bot/actions/workflows/ci.yml/badge.svg)](https://github.com/viral32111/twitch-bot/actions/workflows/ci.yml)
[![CodeQL](https://github.com/viral32111/twitch-bot/actions/workflows/codeql.yml/badge.svg)](https://github.com/viral32111/twitch-bot/actions/workflows/codeql.yml)
![GitHub tag (with filter)](https://img.shields.io/github/v/tag/viral32111/TwitchBot?label=Latest)
![GitHub repository size](https://img.shields.io/github/repo-size/viral32111/TwitchBot?label=Size)
![GitHub release downloads](https://img.shields.io/github/downloads/viral32111/TwitchBot/total?label=Downloads)
![GitHub commit activity](https://img.shields.io/github/commit-activity/m/viral32111/TwitchBot?label=Commits)

A Twitch integration & chat bot made for [RawrelTV](https://twitch.tv/rawreltv).

## 📥 Usage

The recommended way to run this bot is by using the [Docker image](https://github.com/users/viral32111/packages/container/package/twitchbot). This image is automatically updated every time a commit is pushed.

For example, this command will run the bot in a Docker container using a custom configuration file in the current directory:

```
docker run \
	--name twitch-bot \
	--mount type=volume,source=twitch-bot,target=/var/lib/twitch-bot \
	--mount type=bind,source=$PWD/config.json,target=/etc/twitch-bot.json \
	ghcr.io/viral32111/twitch-bot:latest
```

### ⚙️ Configuration

The configuration file is where you should specify Twitch application credentials, channel names, etc.

The default configuration file will be created at the default path, or at the path given as the first command-line argument to the program.

The default path for the configuration file for each operating system is:
 * Windows: `%CD%/twitch-bot.json`
 * Linux: `$PWD/twitch-bot.json`

### Persistent Data

The bot will create data over time that must be retained across reboots.

The location of this directory can be changed in the configuration file. The default for each operating system is:
 * Windows: `%LOCALAPPDATA%/TwitchBot`
 * Linux: `/var/lib/twitch-bot`
 
### Cached Data

The bot will create temporary data over time that is reused across reboots, but can safely be destroyed when closed.

The location of this directory can be changed in the configuration file. The default for each operating system is:
 * Windows: `%TEMP%/TwitchBot`
 * Linux: `/var/cache/twitch-bot`

### 🏗️ Development

When running during development, it is preferred to keep secrets in the [.NET user secrets store](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets).

These secrets can be set using the `dotnet user-secrets set` command. The following secrets are required:
 * `AppClientSecret` should be your Twitch application's client secret.

## ⚖️ License

Copyright (C) 2022 [viral32111](https://viral32111.com).

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as
published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program. If not, see https://www.gnu.org/licenses.
