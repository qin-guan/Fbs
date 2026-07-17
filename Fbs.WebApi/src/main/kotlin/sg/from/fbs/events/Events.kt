package sg.from.fbs.events

import java.time.OffsetDateTime
import java.util.UUID

data class BookingCreatedEvent(
    val id: UUID,
    val facilityName: String?,
    val conduct: String?,
    val description: String?,
    val pocName: String?,
    val pocPhone: String?,
    val startDateTime: OffsetDateTime?,
    val endDateTime: OffsetDateTime?,
    val userPhone: String?
)

data class BookingUpdatedEvent(
    val id: UUID,
    val facilityName: String?,
    val conduct: String?,
    val description: String?,
    val pocName: String?,
    val pocPhone: String?,
    val startDateTime: OffsetDateTime?,
    val endDateTime: OffsetDateTime?,
    val userPhone: String?
)

data class BookingDeletedEvent(
    val id: UUID,
    val facilityName: String?,
    val conduct: String?,
    val description: String?,
    val pocName: String?,
    val pocPhone: String?,
    val startDateTime: OffsetDateTime?,
    val endDateTime: OffsetDateTime?,
    val userPhone: String?
)