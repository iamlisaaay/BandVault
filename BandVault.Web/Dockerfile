FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["BandVault.Web.csproj", "./"]
RUN dotnet restore "BandVault.Web.csproj"

COPY . .
RUN dotnet publish "BandVault.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# B8: QuestPDF (генерація PDF-інвойсів) потребує fontconfig у Linux-контейнері
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BandVault.Web.dll"]
