plugins {
	kotlin("jvm") version "2.3.21"
	kotlin("plugin.spring") version "2.3.21"
	id("org.springframework.boot") version "4.1.0"
	id("io.spring.dependency-management") version "1.1.7"
}

group = "sg.from.fbs"
version = "0.0.1-SNAPSHOT"

java {
	toolchain {
		languageVersion = JavaLanguageVersion.of(21)
	}
}

repositories {
	mavenCentral()
}

dependencies {
	implementation("org.springframework.boot:spring-boot-starter-cache")
	implementation("org.springframework.boot:spring-boot-starter-validation")
	implementation("org.springframework.boot:spring-boot-starter-webmvc")
	implementation("tools.jackson.module:jackson-module-kotlin")
	implementation("org.jetbrains.kotlin:kotlin-reflect")
	implementation("org.springdoc:springdoc-openapi-starter-webmvc-scalar:3.0.3")

	// Google APIs
	implementation("com.google.api-client:google-api-client:2.4.0")
	implementation("com.google.apis:google-api-services-calendar:v3-rev20231123-2.0.0")
	implementation("com.google.apis:google-api-services-sheets:v4-rev20230815-2.0.0")
	implementation("com.google.auth:google-auth-library-oauth2-http:1.23.0")

	// Crypto & JWT
	implementation("org.bouncycastle:bcprov-jdk18on:1.77")
	implementation("io.jsonwebtoken:jjwt-api:0.12.5")
	runtimeOnly("io.jsonwebtoken:jjwt-impl:0.12.5")
	runtimeOnly("io.jsonwebtoken:jjwt-jackson:0.12.5")

	// Caching
	implementation("com.github.ben-manes.caffeine:caffeine:3.1.8")

	testImplementation("org.springframework.boot:spring-boot-starter-cache-test")
	testImplementation("org.springframework.boot:spring-boot-starter-validation-test")
	testImplementation("org.springframework.boot:spring-boot-starter-webmvc-test")
	testImplementation("org.jetbrains.kotlin:kotlin-test-junit5")
	testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

kotlin {
	compilerOptions {
		freeCompilerArgs.addAll("-Xjsr305=strict", "-Xannotation-default-target=param-property")
	}
}

tasks.withType<Test> {
	useJUnitPlatform()
}
