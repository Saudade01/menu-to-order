FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data/keys && chown -R app /data
# Hosts terminate HTTPS in front of the app; trusting X-Forwarded-Proto keeps QR links on https.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/data/keys
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "MenuToOrder.Api.dll"]
