package sg.from.fbs.config

import com.google.api.client.googleapis.javanet.GoogleNetHttpTransport
import com.google.api.client.json.gson.GsonFactory
import com.google.api.services.calendar.Calendar
import com.google.api.services.sheets.v4.Sheets
import com.google.auth.http.HttpCredentialsAdapter
import com.google.auth.oauth2.GoogleCredentials
import org.springframework.context.annotation.Bean
import org.springframework.context.annotation.Configuration
import java.io.ByteArrayInputStream
import java.util.Base64

@Configuration
class GoogleConfig {

    @Bean
    fun calendarService(googleProperties: GoogleProperties): Calendar {
        val credential = createScopedCredentials(
            googleProperties,
            listOf(
                "https://www.googleapis.com/auth/calendar",
                "https://www.googleapis.com/auth/calendar.events"
            )
        )

        return Calendar.Builder(
            GoogleNetHttpTransport.newTrustedTransport(),
            GsonFactory.getDefaultInstance(),
            HttpCredentialsAdapter(credential)
        ).build()
    }

    @Bean
    fun sheetsService(googleProperties: GoogleProperties): Sheets {
        val credential = createScopedCredentials(
            googleProperties,
            listOf("https://www.googleapis.com/auth/spreadsheets")
        )

        return Sheets.Builder(
            GoogleNetHttpTransport.newTrustedTransport(),
            GsonFactory.getDefaultInstance(),
            HttpCredentialsAdapter(credential)
        ).build()
    }

    private fun createScopedCredentials(
        googleProperties: GoogleProperties,
        scopes: List<String>
    ): GoogleCredentials {
        val fallbackJson = """{"type":"service_account"}"""
        val credentialBytes = if (googleProperties.serviceAccountJsonCredential.isNotBlank()) {
            Base64.getDecoder().decode(googleProperties.serviceAccountJsonCredential)
        } else {
            fallbackJson.toByteArray()
        }

        return GoogleCredentials.fromStream(ByteArrayInputStream(credentialBytes))
            .createScoped(scopes)
    }
}