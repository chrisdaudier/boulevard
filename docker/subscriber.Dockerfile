FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Edge/Boulevard.Edge.MarketData/Boulevard.Edge.MarketData.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
RUN apt-get update && apt-get install -y iproute2 iputils-ping
ENTRYPOINT ["dotnet", "Boulevard.Edge.MarketData.dll"]
