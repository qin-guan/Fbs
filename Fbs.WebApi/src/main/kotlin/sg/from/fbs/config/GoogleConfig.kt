package sg.from.fbs.config

import com.google.api.client.googleapis.auth.oauth2.GoogleCredential
import com.google.api.client.googleapis.javanet.GoogleNetHttpTransport
import com.google.api.client.json.gson.GsonFactory
import com.google.api.services.calendar.Calendar
import com.google.api.services.sheets.v4.Sheets
import org.springframework.context.annotation.Bean
import org.springframework.context.annotation.Configuration
import java.io.ByteArrayInputStream
import java.util.Base64

@Configuration
class GoogleConfig {

    @Bean
    fun calendarService(googleProperties: GoogleProperties): Calendar {
        val json = """{"type": "service_account"}"""
        val credentialBytes = if (googleProperties.serviceAccountJsonCredential.isNotBlank()) {
            Base64.getDecoder().decode(googleProperties.serviceAccountJsonCredential)
        } else {
            json.toByteArray()
        }

        val credential = GoogleCredential.fromStream(ByteArrayInputStream(credentialBytes))
            .createScoped(
                listOf(
                    "https://www.googleapis.com/auth/calendar",
                    "https://www.googleapis.com/auth/calendar.events"
                )
            )

        return Calendar.Builder(
            GoogleNetHttpTransport.newTrustedTransport(),
            GsonFactory.getDefaultInstance(),
            credential
        ).build()
    }

    @Bean
    fun sheetsService(googleProperties: GoogleProperties): Sheets {
        val json = """{"type": "service_account"}"""
        val credentialBytes = if (googleProperties.serviceAccountJsonCredential.isNotBlank()) {
            Base64.getDecoder().decode(googleProperties.serviceAccountJsonCredential)
        } else {
            json.toByteArray()
        }

        val credential = GoogleCredential.fromStream(ByteArrayInputStream(credentialBytes))

        return Sheets.Builder(
            GoogleNetHttpTransport.newTrustedTransport(),
            GsonFactory.getDefaultInstance(),
            credential
        ).build()
    }
}