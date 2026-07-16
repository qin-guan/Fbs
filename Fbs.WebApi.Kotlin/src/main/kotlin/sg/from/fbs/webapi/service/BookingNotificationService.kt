package sg.from.fbs.webapi.service

import org.springframework.stereotype.Service
import sg.from.fbs.webapi.event.BookingEvent
import sg.from.fbs.webapi.integration.TelegramClient
import sg.from.fbs.webapi.repository.UserRepository

@Service
class BookingNotificationService(
    private val userRepository: UserRepository,
    private val telegramClient: TelegramClient,
) {
    private val purpleLightLyrics = """
        <i>
        Purple light
        In the valley
        That is where, I wanna be
        Infantry, best companions
        (With my rifle and my buddy and me)
        </i>
    """.trimIndent()

    fun created(event: BookingEvent) = notify("CREATED", "Booked by", event)

    fun updated(event: BookingEvent) = notify("UPDATED", "Updated by", event)

    fun deleted(event: BookingEvent) = notify("CANCELLED", "Cancelled by", event)

    private fun notify(status: String, actorLabel: String, event: BookingEvent) {
        val users = userRepository.getList()
        val actor = userRepository.getByPhone(event.userPhone)
        val description = event.description?.takeIf { it.isNotBlank() } ?: purpleLightLyrics
        val text = """
            <b>$status</b> booking for <b>${event.facilityName}</b>!

            <u>Conduct</u>
            ${event.conduct ?: ""}

            <u>From</u>
            ${event.startDateTime?.toLocalDateTime()}

            <u>To</u>
            ${event.endDateTime?.toLocalDateTime()}

            <u>Point of contact</u>
            Name: ${event.pocName ?: ""}
            Contact: ${event.pocPhone ?: ""}

            <u>$actorLabel</u>
            Unit: ${actor.unit}
            Name: ${actor.name}
            Contact: ${actor.phone}

            <u>Description</u>
            $description

            <u>Confirmation</u>
            ${event.id}
        """.trimIndent()

        actor.telegramChatId?.let { telegramClient.sendMessage(it, text, "HTML") }

        users.asSequence()
            .filter { !it.telegramChatId.isNullOrBlank() }
            .filter { it.phone != event.userPhone }
            .filter { it.notificationGroup == "All" || (it.notificationGroup == "Unit" && it.unit == actor.unit) }
            .forEach { telegramClient.sendMessage(it.telegramChatId!!, text, "HTML") }
    }
}
