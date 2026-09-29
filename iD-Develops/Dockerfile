# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "./iD-Develops.csproj"
RUN dotnet publish "./iD-Develops.csproj" -c Release -o /app/publish

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP_VERSION=unknown
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV App__Version=${APP_VERSION}
LABEL org.opencontainers.image.revision=${APP_VERSION}
ENTRYPOINT ["dotnet", "iD_Develops.dll"]
