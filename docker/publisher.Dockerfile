FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Simulators/Boulevard.Simulators.Nasdaq/Boulevard.Simulators.Nasdaq.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# iproute2 provides tc (netem latency injection); iputils-ping is for connectivity testing.
RUN apt-get update && apt-get install -y iproute2 iputils-ping
ENTRYPOINT ["dotnet", "Boulevard.Simulators.Nasdaq.dll"]
