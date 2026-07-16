package sg.from.fbs.repository

import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule
import com.fasterxml.jackson.module.kotlin.jacksonObjectMapper
import com.google.api.client.util.DateTime
import com.google.api.services.calendar.Calendar
import com.google.api.services.calendar.model.Event
import com.google.api.services.calendar.model.EventDateTime
import org.springframework.cache.annotation.CacheEvict
import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.config.GoogleProperties
import sg.from.fbs.entities.Booking
import java.time.OffsetDateTime
import java.util.Base64
import java.util.UUID
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Lazy

@Repository
class BookingRepository(
    private val calendarService: Calendar,
    private val options: GoogleProperties,
    private val userRepository: UserRepository
) {
    @Autowired
    @Lazy
    lateinit var self: BookingRepository

    private val objectMapper: ObjectMapper = jacksonObjectMapper().registerModule(JavaTimeModule())

    @Cacheable("Bookings")
    fun getList(): List<Booking> {
        var token: String? = null
        val bookings = mutableListOf<Booking>()

        do {
            val request = calendarService.events().list(options.calendarId)
            if (token != null) {
                request.pageToken = token
            }

            val items = request.execute()
            token = items.nextPageToken

            val converted = items.items.mapNotNull { item ->
                try {
                    val data = item.extendedProperties?.shared?.get("Data") ?: return@mapNotNull null
                    val bytes = Base64.getDecoder().decode(data)

                    val booking: Booking = try {
                        objectMapper.readValue(String(bytes), Booking::class.java)
                    } catch (e: Exception) {
                        // Fallback: Extract from Google Event attributes since MemoryPack is binary
                        val b = Booking()
                        b.id = try { UUID.fromString(item.id) } catch (ex: Exception) { UUID.randomUUID() }
                        b.facilityName = item.location

                        // Parse description
                        // Point of contact: {pocName} / {pocPhone}
                        // Booked by: {unit} / {name}
                        // Number: {userPhone}
                        // Description: \n{description}
                        val desc = item.description ?: ""
                        val pocRegex = Regex("Point of contact: (.*) / (.*)")
                        val pocMatch = pocRegex.find(desc)
                        b.pocName = pocMatch?.groupValues?.get(1)?.trim()
                        b.pocPhone = pocMatch?.groupValues?.get(2)?.trim()

                        val numberRegex = Regex("Number: (.*)")
                        val numberMatch = numberRegex.find(desc)
                        b.userPhone = numberMatch?.groupValues?.get(1)?.trim()

                        val descriptionRegex = Regex("Description: \\s*(.*)", RegexOption.DOT_MATCHES_ALL)
                        val descriptionMatch = descriptionRegex.find(desc)
                        b.description = descriptionMatch?.groupValues?.get(1)?.trim()

                        // Summary: {unit} {conduct}
                        val summaryParts = item.summary?.split(" ", limit = 2)
                        b.conduct = if (summaryParts != null && summaryParts.size > 1) summaryParts[1] else item.summary

                        b
                    }

                    val eventStartDateTime = item.start?.dateTime?.value?.let {
                        OffsetDateTime.parse(item.start.dateTime.toStringRfc3339())
                    }
                    val eventEndDateTime = item.end?.dateTime?.value?.let {
                        OffsetDateTime.parse(item.end.dateTime.toStringRfc3339())
                    }

                    if (eventStartDateTime == null || eventEndDateTime == null) {
                        return@mapNotNull null
                    }

                    booking.startDateTime = eventStartDateTime
                    booking.endDateTime = eventEndDateTime
                    booking
                } catch (e: Exception) {
                    null
                }
            }

            bookings.addAll(converted)
        } while (token != null)

        return bookings
    }

    fun find(predicate: (Booking) -> Boolean): Booking? {
        return self.getList().find(predicate)
    }

    fun get(predicate: (Booking) -> Boolean): Booking {
        return self.getList().first(predicate)
    }

    @CacheEvict(value = ["Bookings"], allEntries = true)
    fun insert(entity: Booking): Booking {
        val user = userRepository.get { it.phone == entity.userPhone }

        val data = Base64.getEncoder().encodeToString(objectMapper.writeValueAsBytes(entity))

        if (data.length > 1000) {
            throw Exception("Event information is too long.")
        }

        val event = Event().apply {
            id = entity.id.toString().replace("-", "")
            summary = "${user.unit} ${entity.conduct}"
            start = EventDateTime().setDateTime(DateTime(entity.startDateTime.toString()))
            end = EventDateTime().setDateTime(DateTime(entity.endDateTime.toString()))
            location = entity.facilityName
            description = """
                Point of contact: ${entity.pocName} / ${entity.pocPhone}

                Booked by: ${user.unit} / ${user.name}
                Number: ${user.phone}

                Description:
                ${entity.description}
            """.trimIndent()
            extendedProperties = Event.ExtendedProperties().setShared(mapOf("Data" to data))
        }

        calendarService.events().insert(options.calendarId, event).execute()
        calendarService.events().insert(options.carbonCopyCalendarId, event).execute()

        return entity
    }

    @CacheEvict(value = ["Bookings"], allEntries = true)
    fun update(entity: Booking): Booking {
        val bookings = self.getList()
        val booking = bookings.single { it.id == entity.id }

        booking.startDateTime = entity.startDateTime
        booking.endDateTime = entity.endDateTime
        booking.conduct = entity.conduct
        booking.description = entity.description
        booking.facilityName = entity.facilityName
        booking.pocName = entity.pocName
        booking.pocPhone = entity.pocPhone
        booking.userPhone = entity.userPhone

        val data = Base64.getEncoder().encodeToString(objectMapper.writeValueAsBytes(booking))

        if (data.length > 1000) {
            throw Exception("Event information is too long.")
        }

        val user = userRepository.get { it.phone == booking.userPhone }

        val event = Event().apply {
            id = booking.id.toString().replace("-", "")
            summary = "${user.unit} ${booking.conduct}"
            start = EventDateTime().setDateTime(DateTime(booking.startDateTime.toString()))
            end = EventDateTime().setDateTime(DateTime(booking.endDateTime.toString()))
            location = booking.facilityName
            description = """
                Point of contact: ${booking.pocName} / ${booking.pocPhone}

                Booked by: ${user.unit} / ${user.name}
                Number: ${user.phone}

                Description:
                ${booking.description}
            """.trimIndent()
            extendedProperties = Event.ExtendedProperties().setShared(mapOf("Data" to data))
        }

        calendarService.events().update(options.calendarId, event.id, event).execute()
        calendarService.events().update(options.carbonCopyCalendarId, event.id, event).execute()

        return booking
    }

    @CacheEvict(value = ["Bookings"], allEntries = true)
    fun delete(predicate: (Booking) -> Boolean) {
        val booking = self.get(predicate)
        val eventId = booking.id.toString().replace("-", "")

        calendarService.events().delete(options.calendarId, eventId).execute()
        calendarService.events().delete(options.carbonCopyCalendarId, eventId).execute()
    }
}