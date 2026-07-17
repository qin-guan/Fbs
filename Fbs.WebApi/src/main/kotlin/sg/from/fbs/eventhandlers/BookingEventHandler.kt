package sg.from.fbs.eventhandlers

import org.springframework.context.event.EventListener
import org.springframework.scheduling.annotation.Async
import org.springframework.stereotype.Component
import org.springframework.web.client.RestTemplate
import sg.from.fbs.config.TelegramProperties
import sg.from.fbs.events.BookingCreatedEvent
import sg.from.fbs.events.BookingDeletedEvent
import sg.from.fbs.events.BookingUpdatedEvent
import sg.from.fbs.repository.UserRepository
import org.springframework.web.util.HtmlUtils
import java.time.format.DateTimeFormatter
import java.time.ZoneId

@Component
class BookingEventHandler(
    private val userRepository: UserRepository,
    private val telegramProperties: TelegramProperties
) {

    private val restTemplate = RestTemplate()

    private val purpleLightLyrics = """
        <i>
        Purple light
        In the valley
        That is where, I wanna be
        Infantry, best companions
        (With my rifle and my buddy and me)
        </i>
    """.trimIndent()

    private val formatter = DateTimeFormatter.ofPattern("EEEE, d MMMM yyyy, h:mm a").withZone(ZoneId.systemDefault())

    @Async
    @EventListener
    fun handleBookingCreated(event: BookingCreatedEvent) {
        val users = userRepository.getList()
        val user = userRepository.get { it.phone == event.userPhone }

        val subscribedUsers = users
            .filter { !it.telegramChatId.isNullOrBlank() }
            .filter { it.phone != event.userPhone }
            .filter { it.notificationGroup == "All" || (it.notificationGroup == "Unit" && it.unit == user.unit) }

        val desc = if (event.description.isNullOrBlank()) purpleLightLyrics else HtmlUtils.htmlEscape(event.description!!)
        val message = """
            <b>CREATED</b> booking for <b>${event.facilityName}</b>!

            <u>Conduct</u>
            ${HtmlUtils.htmlEscape(event.conduct ?: "")}

            <u>From</u>
            ${event.startDateTime?.let { formatter.format(it) }}

            <u>To</u>
            ${event.endDateTime?.let { formatter.format(it) }}

            <u>Point of contact</u>
            Name: ${HtmlUtils.htmlEscape(event.pocName ?: "")}
            Contact: ${HtmlUtils.htmlEscape(event.pocPhone ?: "")}

            <u>Booked by</u>
            Unit: ${user.unit}
            Name: ${user.name}
            Contact: ${user.phone}

            <u>Description</u>
            $desc

            <u>Confirmation</u>
            ${event.id}
        """.trimIndent()

        sendMessage(user.telegramChatId, message)
        for (u in subscribedUsers) {
            sendMessage(u.telegramChatId, message)
        }
    }

    @Async
    @EventListener
    fun handleBookingUpdated(event: BookingUpdatedEvent) {
        val user = userRepository.get { it.phone == event.userPhone }

        val desc = if (event.description.isNullOrBlank()) purpleLightLyrics else HtmlUtils.htmlEscape(event.description!!)
        val message = """
            <b>UPDATED</b> booking for <b>${event.facilityName}</b>!

            <u>Conduct</u>
            ${HtmlUtils.htmlEscape(event.conduct ?: "")}

            <u>From</u>
            ${event.startDateTime?.let { formatter.format(it) }}

            <u>To</u>
            ${event.endDateTime?.let { formatter.format(it) }}

            <u>Point of contact</u>
            Name: ${HtmlUtils.htmlEscape(event.pocName ?: "")}
            Contact: ${HtmlUtils.htmlEscape(event.pocPhone ?: "")}

            <u>Booked by</u>
            Unit: ${user.unit}
            Name: ${user.name}
            Contact: ${user.phone}

            <u>Description</u>
            $desc

            <u>Confirmation</u>
            ${event.id}
        """.trimIndent()

        // FastEndpoints didn't implement send logic for Update/Delete in the provided snippets (it only had BookingCreatedEventHandler.cs)
        // We will send to user to match potential expected behaviour.
        sendMessage(user.telegramChatId, message)
    }

    @Async
    @EventListener
    fun handleBookingDeleted(event: BookingDeletedEvent) {
        val user = userRepository.get { it.phone == event.userPhone }

        val desc = if (event.description.isNullOrBlank()) purpleLightLyrics else HtmlUtils.htmlEscape(event.description!!)
        val message = """
            <b>DELETED</b> booking for <b>${event.facilityName}</b>!

            <u>Conduct</u>
            ${HtmlUtils.htmlEscape(event.conduct ?: "")}

            <u>From</u>
            ${event.startDateTime?.let { formatter.format(it) }}

            <u>To</u>
            ${event.endDateTime?.let { formatter.format(it) }}

            <u>Point of contact</u>
            Name: ${HtmlUtils.htmlEscape(event.pocName ?: "")}
            Contact: ${HtmlUtils.htmlEscape(event.pocPhone ?: "")}

            <u>Booked by</u>
            Unit: ${user.unit}
            Name: ${user.name}
            Contact: ${user.phone}

            <u>Description</u>
            $desc

            <u>Confirmation</u>
            ${event.id}
        """.trimIndent()

        sendMessage(user.telegramChatId, message)
    }

    private fun sendMessage(chatId: String?, text: String) {
        if (chatId.isNullOrBlank()) return
        try {
            restTemplate.postForObject(
                "https://api.telegram.org/bot${telegramProperties.token}/sendMessage",
                mapOf(
                    "chat_id" to chatId,
                    "text" to text,
                    "parse_mode" to "Html"
                ),
                String::class.java
            )
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }
}