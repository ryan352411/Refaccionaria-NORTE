FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CatalogoWeb/CatalogoWeb.csproj CatalogoWeb/
RUN dotnet restore CatalogoWeb/CatalogoWeb.csproj

COPY CatalogoWeb/ CatalogoWeb/
RUN dotnet publish CatalogoWeb/CatalogoWeb.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .
EXPOSE 10000

ENTRYPOINT ["sh", "-c", "dotnet CatalogoWeb.dll --urls http://0.0.0.0:${PORT:-10000}"]
