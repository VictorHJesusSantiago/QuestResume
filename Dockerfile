# Builds and runs QuestResume.Api. Build from the repository root:
#   docker build -t questresume-api .
#   docker run -p 8080:8080 -v questresume-data:/home/appuser/.local/share/QuestResume questresume-api
#
# OCR (Tesseract) and STT (Whisper.net) native dependencies aren't preinstalled in this image;
# those features stay gracefully disabled until the relevant config paths are set up inside the
# container (see README.md for details).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/QuestResume.Core/QuestResume.Core.csproj src/QuestResume.Core/
COPY src/QuestResume.Api/QuestResume.Api.csproj src/QuestResume.Api/
RUN dotnet restore src/QuestResume.Api/QuestResume.Api.csproj

COPY src/QuestResume.Core/ src/QuestResume.Core/
COPY src/QuestResume.Api/ src/QuestResume.Api/
RUN dotnet publish src/QuestResume.Api/QuestResume.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .

# Run as a non-root user. The data directory is created here so the volume mount lands with
# the correct ownership when the container starts.
RUN adduser --disabled-password --gecos "" --uid 1000 appuser \
    && mkdir -p /home/appuser/.local/share/QuestResume \
    && chown -R appuser /home/appuser
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "QuestResume.Api.dll"]
