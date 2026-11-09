# Built from the repository root: docker build -f src/BergerHarbour.PublicApi/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props BergerHarbour.slnx ./
COPY src/ src/
RUN dotnet publish src/BergerHarbour.PublicApi/BergerHarbour.PublicApi.csproj -c Release -o /app \
    -r linux-x64 --self-contained false -p:PublishReadyToRun=true -p:OpenApiGenerateDocuments=false \
 && chmod -R a+rX /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "BergerHarbour.PublicApi.dll"]
