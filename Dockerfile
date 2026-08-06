FROM eclipse-temurin:21-jdk-alpine AS build
WORKDIR /src
COPY Fbs.WebApi/ .
RUN ./gradlew build -x test

FROM eclipse-temurin:21-jre-alpine AS final
WORKDIR /app
COPY --from=build /src/build/libs/*.jar app.jar
EXPOSE 8080
ENTRYPOINT ["java", "-jar", "app.jar"]