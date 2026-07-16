package sg.from.fbs.webapi.controller

import com.fasterxml.jackson.databind.JsonNode
import org.springframework.cache.CacheManager
import org.springframework.http.ResponseEntity
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.RequestBody
import org.springframework.web.bind.annotation.RestController
import sg.from.fbs.webapi.repository.NominalRollRepository
import sg.from.fbs.webapi.repository.UserRepository
import sg.from.fbs.webapi.integration.TelegramClient

@RestController
class SupportController(
    private val nominalRollRepository: NominalRollRepository,
    private val userRepository: UserRepository,
    private val telegramClient: TelegramClient,
    private val cacheManager: CacheManager,
) {
    @GetMapping("/NominalRoll")
    fun nominalRoll() = nominalRollRepository.getList().distinctBy { it.phone }

    @GetMapping("/Cache/Purge")
    fun purge(): ResponseEntity<Void> {
        listOf("Facilities", "Bookings", "Nominal Roll", "Users").forEach { cacheManager.getCache(it)?.clear() }
        return ResponseEntity.ok().build()
    }

    @PostMapping("/Bot")
    fun bot(@RequestBody req: JsonNode): ResponseEntity<Void> {
        val message = req.path("message")
        if (message.isMissingNode) return ResponseEntity.ok().build()
        val chatId = message.path("chat").path("id").asText()

        val contactPhone = message.path("contact").path("phone_number").asText().ifBlank { null }
        when {
            contactPhone != null -> {
                val normalized = contactPhone.removePrefix("+")
                val user = userRepository.findByPhone(normalized)
                if (user == null) {
                    telegramClient.sendMessage(
                        chatId,
                        """
                        Thank you!

                        Your number has not been whitelisted. Please approach your unit S3 for whitelisting.
                        """.trimIndent(),
                    )
                } else {
                    user.telegramChatId = chatId
                    userRepository.update(user)
                    telegramClient.sendMessage(
                        chatId,
                        """
                        You've been successfully registered as ${user.name}!

                        You may now use Telegram to authenticate with the Facility Booking System.

                        Make a booking <a href="https://3sib-fbs.from.sg">here</a>.
                        """.trimIndent(),
                        parseMode = "HTML",
                        replyMarkup = mapOf("remove_keyboard" to true),
                    )
                }
            }

            message.path("text").asText() == "/start" -> {
                telegramClient.sendMessage(
                    chatId,
                    """
                    <b>Welcome to 3SIB Facility Booking System</b>

                    Please link your Telegram account clicking the button below =)
                    """.trimIndent(),
                    parseMode = "HTML",
                    replyMarkup = mapOf(
                        "keyboard" to listOf(listOf(mapOf("text" to "Link account", "request_contact" to true))),
                        "resize_keyboard" to true,
                        "one_time_keyboard" to true,
                    ),
                )
            }

            else -> telegramClient.sendMessage(chatId, "Unknown command :(")
        }

        return ResponseEntity.ok().build()
    }
}
