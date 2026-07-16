package sg.from.fbs.webapi.integration

import org.springframework.http.MediaType
import org.springframework.stereotype.Component
import org.springframework.web.client.RestClient
import sg.from.fbs.webapi.config.TelegramProperties

@Component
class TelegramClient(
    private val restClient: RestClient,
    private val telegramProperties: TelegramProperties,
) {
    fun setWebhook(url: String) {
        if (telegramProperties.token.isBlank()) return
        call("setWebhook", mapOf("url" to url))
    }

    fun sendMessage(chatId: String, text: String, parseMode: String? = null, replyMarkup: Any? = null) {
        if (telegramProperties.token.isBlank() || chatId.isBlank()) return
        val body = mutableMapOf<String, Any?>(
            "chat_id" to chatId,
            "text" to text,
            "disable_web_page_preview" to true,
        )
        if (parseMode != null) body["parse_mode"] = parseMode
        if (replyMarkup != null) body["reply_markup"] = replyMarkup
        call("sendMessage", body)
    }

    private fun call(method: String, body: Map<String, Any?>) {
        restClient.post()
            .uri("https://api.telegram.org/bot${telegramProperties.token}/$method")
            .contentType(MediaType.APPLICATION_JSON)
            .body(body)
            .retrieve()
            .toBodilessEntity()
    }
}
