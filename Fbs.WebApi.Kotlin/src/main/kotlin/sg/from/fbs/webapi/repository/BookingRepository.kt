package sg.from.fbs.webapi.repository

import com.fasterxml.jackson.core.type.TypeReference
import com.fasterxml.jackson.databind.JsonNode
import com.fasterxml.jackson.databind.ObjectMapper
import org.springframework.cache.annotation.CacheEvict
import org.springframework.cache.annotation.Cacheable
import org.springframework.stereotype.Repository
import sg.from.fbs.webapi.config.GoogleProperties
import sg.from.fbs.webapi.entity.BookingEntity
import sg.from.fbs.webapi.integration.GoogleCalendarClient
import java.nio.charset.StandardCharsets
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.Base64
import java.util.UUID

@Repository
class BookingRepository(
    private val calendar: GoogleCalendarClient,
    private val props: GoogleProperties,
    private val users: UserRepository,
    private val mapper: ObjectMapper,
) {
    @Cacheable("Bookings")
    fun getList(): List<BookingEntity> =
        calendar.listAllEvents(props.calendarId).mapNotNull(::toBooking)

    fun findById(id: UUID): BookingEntity? = getList().singleOrNull { it.id == id }

    fun getById(id: UUID): BookingEntity = getList().single { it.id == id }

    @CacheEvict("Bookings", allEntries = true)
    fun insert(entity: BookingEntity): BookingEntity {
        entity.id = UUID.randomUUID()
        val payload = calendarPayload(entity)
        calendar.insert(props.calendarId, payload)
        calendar.insert(props.carbonCopyCalendarId, payload)
        return entity
    }

    @CacheEvict("Bookings", allEntries = true)
    fun update(entity: BookingEntity): BookingEntity {
        val existing = getById(entity.id)
        existing.startDateTime = entity.startDateTime
        existing.endDateTime = entity.endDateTime
        existing.conduct = entity.conduct
        existing.description = entity.description
        existing.facilityName = entity.facilityName
        existing.pocName = entity.pocName
        existing.pocPhone = entity.pocPhone
        existing.userPhone = entity.userPhone
        val payload = calendarPayload(existing)
        val eventId = existing.id.toString().replace("-", "")
        calendar.update(props.calendarId, eventId, payload)
        calendar.update(props.carbonCopyCalendarId, eventId, payload)
        return existing
    }

    @CacheEvict("Bookings", allEntries = true)
    fun delete(id: UUID) {
        val eventId = id.toString().replace("-", "")
        calendar.delete(props.calendarId, eventId)
        calendar.delete(props.carbonCopyCalendarId, eventId)
    }

    private fun calendarPayload(booking: BookingEntity): Map<String, Any?> {
        val user = users.getByPhone(booking.userPhone)
        val compact = mapper.writeValueAsString(booking)
        val data = Base64.getEncoder().encodeToString(compact.toByteArray(StandardCharsets.UTF_8))
        require(data.length <= 1000) { "Event information is too long." }
        return mapOf(
            "id" to booking.id.toString().replace("-", ""),
            "summary" to "${user.unit ?: ""} ${booking.conduct ?: ""}".trim(),
            "start" to mapOf("dateTime" to booking.startDateTime?.toString()),
            "end" to mapOf("dateTime" to booking.endDateTime?.toString()),
            "location" to booking.facilityName,
            "description" to """
                Point of contact: ${booking.pocName} / ${booking.pocPhone}

                Booked by: ${user.unit} / ${user.name}
                Number: ${user.phone}

                Description:
                ${booking.description}
            """.trimIndent(),
            "extendedProperties" to mapOf("shared" to mapOf("Data" to data)),
        )
    }

    private fun toBooking(event: JsonNode): BookingEntity? {
        val id = event.path("id").asText().takeIf { it.isNotBlank() } ?: return null
        val extendedData = event.path("extendedProperties").path("shared").path("Data").asText()
        val fromExtended = decodeFromExtended(extendedData)
        val start = event.path("start").path("dateTime").asText().takeIf { it.isNotBlank() }?.let(OffsetDateTime::parse)
        val end = event.path("end").path("dateTime").asText().takeIf { it.isNotBlank() }?.let(OffsetDateTime::parse)
        return (fromExtended ?: BookingEntity()).copyWith(
            id = normalizeEventId(id),
            start = start,
            end = end,
            facilityName = event.path("location").asText().ifBlank { fromExtended?.facilityName },
        )
    }

    private fun decodeFromExtended(data: String): BookingEntity? {
        if (data.isBlank()) return null
        return runCatching {
            val decoded = Base64.getDecoder().decode(data)
            mapper.readValue(decoded, object : TypeReference<BookingEntity>() {})
        }.getOrNull()
    }

    private fun normalizeEventId(id: String): UUID =
        runCatching { UUID.fromString(id) }
            .recoverCatching {
                val n = id.lowercase()
                UUID.fromString("${n.substring(0, 8)}-${n.substring(8, 12)}-${n.substring(12, 16)}-${n.substring(16, 20)}-${n.substring(20)}")
            }
            .getOrDefault(UUID.nameUUIDFromBytes(id.toByteArray(StandardCharsets.UTF_8)))

    private fun BookingEntity.copyWith(
        id: UUID,
        start: OffsetDateTime?,
        end: OffsetDateTime?,
        facilityName: String?,
    ): BookingEntity =
        this.copy(
            id = id,
            startDateTime = start ?: this.startDateTime?.withOffsetSameInstant(ZoneOffset.UTC),
            endDateTime = end ?: this.endDateTime?.withOffsetSameInstant(ZoneOffset.UTC),
            facilityName = facilityName,
        )
}
