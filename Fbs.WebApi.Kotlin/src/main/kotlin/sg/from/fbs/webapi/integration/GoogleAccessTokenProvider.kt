package sg.from.fbs.webapi.integration

import com.google.auth.oauth2.GoogleCredentials
import org.springframework.stereotype.Component
import sg.from.fbs.webapi.config.GoogleProperties
import java.io.ByteArrayInputStream
import java.time.Instant
import java.util.Base64

@Component
class GoogleAccessTokenProvider(props: GoogleProperties) {
    private val calendarCredentials: GoogleCredentials
    private val sheetsCredentials: GoogleCredentials

    init {
        val decoded = Base64.getDecoder().decode(props.serviceAccountJsonCredential.ifBlank { "e30=" })
        val base = GoogleCredentials.fromStream(ByteArrayInputStream(decoded))
        calendarCredentials = base.createScoped(listOf("https://www.googleapis.com/auth/calendar", "https://www.googleapis.com/auth/calendar.events"))
        sheetsCredentials = base.createScoped(listOf("https://www.googleapis.com/auth/spreadsheets"))
    }

    @Synchronized
    fun calendarToken(): String = token(calendarCredentials)

    @Synchronized
    fun sheetsToken(): String = token(sheetsCredentials)

    private fun token(credentials: GoogleCredentials): String {
        if (credentials.accessToken == null || credentials.accessToken.expirationTime.toInstant().isBefore(Instant.now().plusSeconds(60))) {
            credentials.refreshIfExpired()
        }
        return credentials.accessToken.tokenValue
    }
}
