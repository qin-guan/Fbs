package sg.from.fbs.webapi.event

import java.time.OffsetDateTime
import java.util.UUID

data class BookingEvent(
    val id: UUID,
    val facilityName: String?,
    val conduct: String?,
    val description: String?,
    val pocName: String?,
    val pocPhone: String?,
    val startDateTime: OffsetDateTime?,
    val endDateTime: OffsetDateTime?,
    val userPhone: String?,
)
