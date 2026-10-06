# Build from the repository root: docker build -f src/BergerHarbour.AdminApi/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props BergerHarbour.slnx ./
COPY src/ src/
RUN dotnet publish src/BergerHarbour.AdminApi -c Release -r linux-x64 --self-contained false \
    -p:PublishReadyToRun=true -p:OpenApiGenerateDocuments=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Cloud Run sets PORT (8080); the app listens on it. Australia/Sydney time zone data ships with the image.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "BergerHarbour.AdminApi.dll"]
