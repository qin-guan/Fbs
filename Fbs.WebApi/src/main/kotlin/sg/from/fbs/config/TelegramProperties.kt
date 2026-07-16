package sg.from.fbs.config

import org.springframework.boot.context.properties.ConfigurationProperties
import org.springframework.context.annotation.Configuration

@Configuration
@ConfigurationProperties(prefix = "telegram")
class TelegramProperties {
    var token = ""
    var webhookUrl = ""
}