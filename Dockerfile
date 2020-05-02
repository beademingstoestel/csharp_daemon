FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env

ARG build_version=1.0.0.0
ENV build_version=$build_version

WORKDIR /app

COPY ./VentilatorDaemon/VentilatorDaemon.csproj .
RUN dotnet restore --disable-parallel

COPY ./VentilatorDaemon .
RUN dotnet publish -c Release -o out /p:AssemblyVersion=$build_version

FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "VentilatorDaemon.dll"]
