FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env

ARG build_number=1
ENV build_number=$build_number

WORKDIR /app

COPY ./VentilatorDaemon/VentilatorDaemon.csproj .
RUN dotnet restore --disable-parallel

COPY ./VentilatorDaemon .
RUN dotnet publish --version-suffix $build_number -c Release -o out

FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "VentilatorDaemon.dll"]