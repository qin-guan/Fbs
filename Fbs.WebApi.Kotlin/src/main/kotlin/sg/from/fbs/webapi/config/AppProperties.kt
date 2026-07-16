package sg.from.fbs.webapi.config

import org.springframework.boot.context.properties.ConfigurationProperties

@ConfigurationProperties("fbs.google")
data class GoogleProperties(
    val serviceAccountJsonCredential: String,
    val spreadsheetId: String,
    val calendarId: String,
    val carbonCopyCalendarId: String,
)

@ConfigurationProperties("fbs.telegram")
data class TelegramProperties(
    val token: String,
    val webhookUrl: String,
)

@ConfigurationProperties("fbs.auth-cookie")
data class AuthCookieProperties(
    val name: String = "fbs_auth",
    val secret: String,
    val domain: String? = null,
    val secure: Boolean = false,
    val maxAgeDays: Long = 1,
)

@ConfigurationProperties("fbs.cors")
data class CorsProperties(
    val origins: List<String> = emptyList(),
)
