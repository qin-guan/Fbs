package sg.from.fbs.controllers

import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.RequestBody
import org.springframework.web.bind.annotation.RequestMapping
import org.springframework.web.bind.annotation.RestController
import org.springframework.web.client.RestTemplate
import sg.from.fbs.config.TelegramProperties
import sg.from.fbs.repository.UserRepository

data class TelegramUpdate(
    val message: TelegramMessage?
)

data class TelegramMessage(
    val chat: TelegramChat?,
    val contact: TelegramContact?,
    val text: String?
)

data class TelegramChat(
    val id: Long
)

data class TelegramContact(
    val phoneNumber: String
)

@RestController
@RequestMapping("/Bot")
class BotController(
    private val userRepository: UserRepository,
    private val telegramProperties: TelegramProperties
) {
    private val restTemplate = RestTemplate()

    @PostMapping
    fun handleUpdate(@RequestBody req: TelegramUpdate): ResponseEntity<Any> {
        val message = req.message ?: return ResponseEntity.ok().build()

        if (message.contact != null) {
            val contact = message.contact
            val normalizedPhoneNumber = if (contact.phoneNumber.startsWith("+")) {
                contact.phoneNumber.substring(1)
            } else {
                contact.phoneNumber
            }

            val user = userRepository.find { it.phone == normalizedPhoneNumber }
            if (user == null) {
                sendMessage(message.chat?.id, """
                    Thank you!

                    Your number has not been whitelisted. Please approach your unit S3 for whitelisting.
                """.trimIndent())
                return ResponseEntity.ok().build()
            }

            user.telegramChatId = message.chat?.id.toString()
            userRepository.update(user)

            sendMessage(message.chat?.id, """
                You've been successfully registered as ${user.name}!

                You may now use Telegram to authenticate with the Facility Booking System.

                Make a booking <a href="https://3sib-fbs.from.sg">here</a>.
            """.trimIndent(), parseMode = "Html")
        } else if (message.text == "/start") {
            try {
                restTemplate.postForObject(
                    "https://api.telegram.org/bot${telegramProperties.token}/sendMessage",
                    mapOf(
                        "chat_id" to message.chat?.id,
                        "text" to """
                            <b>Welcome to 3SIB Facility Booking System</b>

                            Please link your Telegram account clicking the button below =)
                        """.trimIndent(),
                        "parse_mode" to "Html",
                        "reply_markup" to mapOf(
                            "keyboard" to listOf(
                                listOf(
                                    mapOf(
                                        "text" to "Link account",
                                        "request_contact" to true
                                    )
                                )
                            )
                        )
                    ),
                    String::class.java
                )
            } catch (e: Exception) {
                e.printStackTrace()
            }
        } else {
            sendMessage(message.chat?.id, "Unknown command :(")
        }

        return ResponseEntity.ok().build()
    }

    private fun sendMessage(chatId: Long?, text: String, parseMode: String? = null) {
        if (chatId == null) return
        val body = mutableMapOf(
            "chat_id" to chatId,
            "text" to text,
            "reply_markup" to mapOf("remove_keyboard" to true)
        )
        if (parseMode != null) {
            body["parse_mode"] = parseMode
        }
        try {
            restTemplate.postForObject(
                "https://api.telegram.org/bot${telegramProperties.token}/sendMessage",
                body,
                String::class.java
            )
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }
}