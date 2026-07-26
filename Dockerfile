FROM eclipse-temurin:21-jdk-alpine AS build
WORKDIR /src
COPY . .
RUN ./gradlew :Fbs.WebApi:build -x test

FROM eclipse-temurin:21-jre-alpine AS final
WORKDIR /app
COPY --from=build /src/Fbs.WebApi/build/libs/*.jar app.jar
EXPOSE 8080
ENTRYPOINT ["java", "-jar", "app.jar"]