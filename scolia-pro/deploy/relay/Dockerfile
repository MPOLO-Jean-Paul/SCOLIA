FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Scolia.Transport/ src/Scolia.Transport/
COPY src/Scolia.Relay/ src/Scolia.Relay/
RUN dotnet publish src/Scolia.Relay -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER $APP_UID
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Scolia.Relay.dll"]
