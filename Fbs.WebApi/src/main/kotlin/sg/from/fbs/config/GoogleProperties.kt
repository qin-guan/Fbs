package sg.from.fbs.config

import org.springframework.boot.context.properties.ConfigurationProperties
import org.springframework.context.annotation.Configuration

@Configuration
@ConfigurationProperties(prefix = "google")
class GoogleProperties {
    var serviceAccountJsonCredential = ""
    var calendarId = ""
    var carbonCopyCalendarId = ""
    var spreadsheetId = ""
}