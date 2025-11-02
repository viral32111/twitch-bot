# syntax=docker/dockerfile:1

# Start from ASP.NET Core Runtime
FROM ghcr.io/viral32111/aspnetcore:9.0

# Configure directories & files
ARG TWITCH_BOT_DIRECTORY=/opt/twitch-bot \
	TWITCH_BOT_DATA_DIRECTORY=/var/lib/twitch-bot \
	TWITCH_BOT_CACHE_DIRECTORY=/var/cache/twitch-bot \
	TWITCH_BOT_CONFIG_FILE=/etc/twitch-bot.json

# Add artifacts from build
COPY --chown=${USER_ID}:${USER_ID} ./ ${TWITCH_BOT_DIRECTORY}

# Setup required directories
RUN mkdir --verbose --parents ${TWITCH_BOT_DATA_DIRECTORY} ${TWITCH_BOT_CACHE_DIRECTORY} && \
	chown --changes --recursive ${USER_ID}:${USER_ID} ${TWITCH_BOT_DATA_DIRECTORY} ${TWITCH_BOT_CACHE_DIRECTORY}

# Initialize bot to create the configuration file
#RUN dotnet ${TWITCH_BOT_DIRECTORY}/TwitchBot.dll --init ${TWITCH_BOT_CONFIG_FILE} && \
#	chown --changes --recursive ${USER_ID}:${USER_ID} ${TWITCH_BOT_CONFIG_FILE}

# Switch to the regular user
USER ${USER_ID}:${USER_ID}

# Switch to & persist the daa directory
WORKDIR ${TWITCH_BOT_DATA_DIRECTORY}
VOLUME ${TWITCH_BOT_DATA_DIRECTORY}

# Start the bot when launched
ENTRYPOINT [ "dotnet", "/opt/twitch-bot/TwitchBot.dll" ]
CMD [ "/etc/twitch-bot.json" ]
