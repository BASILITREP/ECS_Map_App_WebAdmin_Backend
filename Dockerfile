FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY EcsFeMappingApi.csproj .
RUN dotnet restore "EcsFeMappingApi.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "EcsFeMappingApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EcsFeMappingApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EcsFeMappingApi.dll"]