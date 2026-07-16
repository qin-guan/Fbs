package sg.from.fbs.webapi.config

import com.github.benmanes.caffeine.cache.Caffeine
import org.springframework.boot.ApplicationRunner
import org.springframework.cache.CacheManager
import org.springframework.cache.annotation.EnableCaching
import org.springframework.cache.caffeine.CaffeineCacheManager
import org.springframework.context.annotation.Bean
import org.springframework.context.annotation.Configuration
import org.springframework.http.client.SimpleClientHttpRequestFactory
import org.springframework.web.client.RestClient
import sg.from.fbs.webapi.integration.TelegramClient
import java.time.Duration

@Configuration
@EnableCaching
class AppConfig {
    @Bean
    fun restClient(): RestClient {
        val factory = SimpleClientHttpRequestFactory()
        factory.setConnectTimeout(Duration.ofSeconds(10))
        factory.setReadTimeout(Duration.ofSeconds(30))
        return RestClient.builder().requestFactory(factory).build()
    }

    @Bean
    fun cacheManager(): CacheManager {
        val manager = CaffeineCacheManager("Facilities", "Bookings", "Nominal Roll", "Users")
        manager.setCaffeine(
            Caffeine.newBuilder()
                .maximumSize(500)
                .expireAfterWrite(Duration.ofMinutes(10)),
        )
        return manager
    }

    @Bean
    fun telegramWebhookInitializer(telegramClient: TelegramClient, props: TelegramProperties) =
        ApplicationRunner {
            if (props.token.isNotBlank() && props.webhookUrl.isNotBlank()) {
                telegramClient.setWebhook(props.webhookUrl)
            }
        }
}
