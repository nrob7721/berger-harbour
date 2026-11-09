# Built from the repository root: docker build -f src/BergerHarbour.PublicApi/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props stylecop.json .editorconfig ./
COPY src/ src/
RUN dotnet publish src/BergerHarbour.PublicApi/BergerHarbour.PublicApi.csproj -c Release -r linux-x64 --self-contained false \
    -p:PublishReadyToRun=true -p:OpenApiGenerateDocuments=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Australia/Sydney dates and AEST/AEDT labels need the time zone database.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BergerHarbour.PublicApi.dll"]
