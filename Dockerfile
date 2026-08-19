# Use official .NET SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy project files
COPY ["DoItG2/DoItG2.csproj", "DoItG2/"]
RUN dotnet restore "DoItG2/DoItG2.csproj"

# Copy everything and build
COPY . .
WORKDIR "/src/DoItG2"
RUN dotnet publish "DoItG2.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Production runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
COPY --from=build /app/publish .

# Environment variables
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DoItG2.dll"]
