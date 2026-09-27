# Compilación con el SDK y ejecución con la imagen mínima de ASP.NET Core.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AnimeTracker.slnx ./
COPY src/AnimeTracker.Domain/AnimeTracker.Domain.csproj src/AnimeTracker.Domain/
COPY src/AnimeTracker.Api/AnimeTracker.Api.csproj src/AnimeTracker.Api/
RUN dotnet restore src/AnimeTracker.Api/AnimeTracker.Api.csproj
COPY src ./src
RUN dotnet publish src/AnimeTracker.Api/AnimeTracker.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
# Carpeta de las claves de sesión con el dueño correcto: el volumen de compose hereda estos permisos.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "AnimeTracker.Api.dll"]
